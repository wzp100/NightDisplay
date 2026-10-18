#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#pragma function(memset)

// No CLR, C/C++ runtime, UI or shell library. Only Kernel32 is imported.
// Protocol v2: four aligned int64 values; synchronized with SafetyLease.cs.
struct Lease { volatile LONG64 heartbeat, virtualRequested, deadline, version; };
static_assert(sizeof(Lease) == 32, "Lease ABI");
extern "C" void* memset(void* destination, int value, size_t size) {
    volatile unsigned char* bytes = static_cast<volatile unsigned char*>(destination);
    while (size--) *bytes++ = static_cast<unsigned char>(value);
    return destination;
}
static LONG64 Read(volatile LONG64* value) { return InterlockedCompareExchange64(value, 0, 0); }
static LONG64 UtcTicks() {
    FILETIME time; GetSystemTimeAsFileTime(&time);
    ULARGE_INTEGER value; value.LowPart = time.dwLowDateTime; value.HighPart = time.dwHighDateTime;
    return static_cast<LONG64>(value.QuadPart) + 504911232000000000LL;
}
static bool Space(wchar_t value) { return value == L' ' || value == L'\t'; }
static bool Argument(const wchar_t*& input, wchar_t* output, DWORD capacity) {
    while (Space(*input)) ++input;
    DWORD size = 0;
    while (*input && !Space(*input)) { if (size + 1 >= capacity) return false; output[size++] = *input++; }
    output[size] = 0; return size != 0;
}
static bool Number(const wchar_t* input, ULONGLONG& value) {
    value = 0;
    while (*input) {
        if (*input < L'0' || *input > L'9' || value > 922337203685477580ULL) return false;
        value = value * 10 + static_cast<ULONGLONG>(*input++ - L'0');
    }
    return value <= 9223372036854775807ULL;
}
static DWORD Restore(const wchar_t* exe, const wchar_t* token) {
    wchar_t* command = static_cast<wchar_t*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, 65536));
    if (!command) return 1;
    lstrcpyW(command, L"\""); lstrcatW(command, exe);
    lstrcatW(command, L"\" --restore --headless --restore-for "); lstrcatW(command, token);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    BOOL started = CreateProcessW(exe, command, nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                                 nullptr, nullptr, &startup, &process);
    HeapFree(GetProcessHeap(), 0, command);
    if (!started) return 1;
    CloseHandle(process.hThread);
    // Keep at most one recovery helper. It exists only during recovery, never at idle.
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD code = 1; GetExitCodeProcess(process.hProcess, &code); CloseHandle(process.hProcess);
    return code;
}
static int Run() {
    const wchar_t* input = GetCommandLineW();
    if (*input == L'"') { ++input; while (*input && *input != L'"') ++input; if (*input) ++input; }
    else while (*input && !Space(*input)) ++input;
    wchar_t pidText[32], timeText[32], token[33]; ULONGLONG pidValue, startValue;
    if (!Argument(input, pidText, 32) || !Argument(input, timeText, 32) || !Argument(input, token, 33) ||
        !Number(pidText, pidValue) || !Number(timeText, startValue) || !pidValue || pidValue > MAXDWORD || lstrlenW(token) != 32) return 2;
    while (Space(*input)) ++input; if (*input) return 2;
    for (int i = 0; i < 32; ++i) if (!((token[i] >= L'0' && token[i] <= L'9') || (token[i] >= L'a' && token[i] <= L'f'))) return 2;
    HANDLE owner = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(pidValue));
    if (!owner) return 3;
    FILETIME created, ended, kernel, user;
    if (!GetProcessTimes(owner, &created, &ended, &kernel, &user)) { CloseHandle(owner); return 3; }
    ULARGE_INTEGER start; start.LowPart = created.dwLowDateTime; start.HighPart = created.dwHighDateTime;
    if (start.QuadPart + 504911232000000000ULL != startValue) { CloseHandle(owner); return 3; }
    wchar_t name[128]; lstrcpyW(name, L"Local\\NightDisplay.Lease."); lstrcatW(name, token);
    HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    Lease* lease = mapping ? static_cast<Lease*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Lease))) : nullptr;
    lstrcatW(name, L".Changed"); HANDLE changed = OpenEventW(SYNCHRONIZE, FALSE, name);
    name[lstrlenW(name) - 8] = 0; lstrcatW(name, L".Ready");
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
    if (!lease || !changed || !ready || Read(&lease->version) != 2) {
        if (lease) UnmapViewOfFile(lease); if (mapping) CloseHandle(mapping);
        if (changed) CloseHandle(changed); if (ready) CloseHandle(ready); CloseHandle(owner); return 4;
    }
    wchar_t* exe = static_cast<wchar_t*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, 65536));
    if (!exe) return 4;
    DWORD size = GetModuleFileNameW(nullptr, exe, 32768);
    if (!size || size > 32000) { HeapFree(GetProcessHeap(), 0, exe); return 4; }
    DWORD index = size; while (index && exe[index - 1] != L'\\' && exe[index - 1] != L'/') --index;
    lstrcpyW(exe + index, L"NightDisplay.exe");
    SetEvent(ready); CloseHandle(ready);
    HANDLE events[] = {owner, changed};
    for (;;) {
        bool isVirtual = Read(&lease->virtualRequested) != 0;
        DWORD result = WaitForMultipleObjects(2, events, FALSE, isVirtual ? 1000 : INFINITE);
        if (result == WAIT_FAILED) break;
        bool alive = WaitForSingleObject(owner, 0) == WAIT_TIMEOUT;
        isVirtual = Read(&lease->virtualRequested) != 0;
        LONG64 now = UtcTicks(), deadline = Read(&lease->deadline);
        bool stale = now - Read(&lease->heartbeat) > 150000000LL;
        if (isVirtual && (!alive || stale || (deadline > 0 && now >= deadline))) {
            DWORD code = Restore(exe, token);
            if (code == 2) break; // A new session already owns the recovery record.
            if (code == 0) { InterlockedExchange64(&lease->virtualRequested, 0); InterlockedExchange64(&lease->deadline, 0); }
            else Sleep(2000);
        }
        if (!alive && Read(&lease->virtualRequested) == 0) break;
    }
    HeapFree(GetProcessHeap(), 0, exe); UnmapViewOfFile(lease);
    CloseHandle(mapping); CloseHandle(changed); CloseHandle(owner); return 0;
}
extern "C" void GuardMain() { ExitProcess(static_cast<UINT>(Run())); }
