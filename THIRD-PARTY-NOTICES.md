# 第三方组件

`driver/` 目录中的虚拟显示器驱动来自 [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)，版本 25.7.23。

- 许可证：MIT
- 版权：Copyright (c) 2024 Virtual Display
- 许可证原文：[`driver/LICENSE.txt`](driver/LICENSE.txt)（随本仓库与发布包一同分发）

按 MIT 许可证要求，版权声明与许可声明随驱动文件的每一份副本保留：仓库中的 `driver/LICENSE.txt` 在 Release 压缩包的 `driver/` 目录内原样保留，本声明文件与项目 [`LICENSE`](LICENSE) 同时包含在发布包中。驱动文件未做修改，重新分发不改变其许可证或署名。若删除 `driver/LICENSE.txt` 或本声明，即违反 MIT 许可证条款。

下载来源：[官方发布包](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip)。INF 声明支持 NTamd64；发布包名称中的 x86 不代表本项目的执行架构。驱动包含上游签名，应用本身未签名。

驱动文件的许可证原文（与 `driver/LICENSE.txt` 一致）：

```text
MIT License

Copyright (c) 2024 Virtual Display

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

`driver/sha256.txt` 是随项目保留的文件校验清单。安装前验证文件内容；SHA-256 校验不能代替驱动发布者的 Authenticode 签名验证。升级驱动必须重新核对上游发布、签名、许可证与清单，禁止通过关闭 Windows 驱动签名验证安装。

应用图标由 OpenAI imagegen 生成，具体设计记录见 `docs/icon-design.md`。应用没有第三方运行时依赖。
