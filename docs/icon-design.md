# 图标设计

设计意图：实体显示器轮廓代表屏幕输出，薄荷绿月牙与星光代表夜间持续工作。深海蓝与薄荷绿用于应用图标、桌面界面与手机页面。

原图：`assets/icons/nightdisplay-master.png`，由原生 OpenAI imagegen 工具生成，透明背景。应用 ICO 包含 16/20/24/32/48/64/128/256 像素，托盘 ICO 包含 16/20/24/32 像素。PNG 用于网页与桌面标题。尺寸转换保留原图，不重新绘制。可替换这些资产后重新构建，不需修改业务代码。

生成提示词：

> Design a finished Windows desktop application icon for NightDisplay, a quiet overnight coding utility switching physical monitors to a virtual display. A crisp minimalist rounded monitor frame in deep navy #132B35 with softly rounded corners, a luminous mint #72E5C0 crescent moon inside the monitor, one tiny mint star, a small sturdy monitor stand. Flat geometric vector-like shapes, restrained premium utility app aesthetic, bold enough to remain legible at 16 and 32 pixels, centered single icon with generous clear padding. No text, no letters, no gradients, no shadows, no background tile. Truly transparent background. Square 1024px master icon.

生成结果的尺寸为 1280×1280。保存透明 PNG 原图，构建使用预先导出的 ICO，无需在用户机器上安装图片处理工具。
