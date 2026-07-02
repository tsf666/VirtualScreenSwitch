# VirtualScreenSwitch (關閉螢幕與鎖定工具)

![License](https://img.shields.io/badge/license-MIT-blue.svg)

---
单文件目录：
```
VirtualScreenSwitch\bin\Release\net8.0-windows\win-x64\publish\VirtualScreenSwitch.exe
```

或者直接点本页面右侧👉→  [Releases](https://github.com/tsf666/VirtualScreenSwitch/releases) 下载

---

# 版本更新记录

v_0.2  修复主题按键亮暗模式
v_0.3  加入自定义秒数及延迟模式选择

---


一個基於 .NET 8.0 Windows 窗體 (WinForms) 開發的輕量化、純綠色免安裝工具。旨在快速執行系統熄屏、鎖定或延時鎖定操作，支援鍵盤快捷鍵、自適應中英文雙語切換以及深淺色主題。

## 📥 運行環境要求 (重要)

本程序採用**框架依賴**方式進行精簡打包（體積僅幾百 KB），運行時需要依賴本機環境。

* **必要條件**：您的電腦必須安裝 **.NET 8.0 或更高版本** 的桌面運行時 (Desktop Runtime)。
* **官方下載指引**：如果雙擊程序時提示缺少環境，請前往微軟官方網站下載並安裝：
  👉 [.NET 8.0 下載頁面](https://dotnet.microsoft.com/download/dotnet/8.0) *(請選擇 **Windows** 平台下的 **Download .NET Desktop Runtime**)*

---

## 🚀 主要功能

* **0 純熄屏**：立刻關閉監視器螢幕，不鎖定系統。
* **1 鎖屏 + 熄屏**：立刻鎖定工作站並關閉螢幕。
* **2 延時鎖屏**：開啟 10 秒倒計時，到點後自動執行 鎖屏+熄屏（中途可隨時中止）。
* **6 暫停**：暫停主界面的自動執行倒計時。
* **3 退出**：直接安全退出本程序。
* **🌐 雙語切換**：右上角一鍵切換中/英文 (ZH/EN)，界面佈局隨文字長度完美自適應，不吞字、不換行。
* **🌓 主題切換**：右上角一鍵切換深色/淺色模式。

---

## ⌨️ 快捷鍵指南 (Choice 監聽)

本程序支持全鍵盤無鼠標操作，等價於原 bat 腳本的 Choice 監聽邏輯：

* **`0` / `1` / `2` / `3` / `6`**：直接觸發對應的底部按鈕功能。
* **`Enter (回車)`**：立刻執行當前選中的默認模式。
* **`↑` / `↓` (方向鍵)**：在 0、1、2 三種默認模式之間循環切換，並自動暫停倒計時。
* **`L`**：切換界面語言。
* **`T`**：切換界面主題。
* **任意其他鍵**：在倒計時已暫停或延時狀態下，按任意鍵可恢復主倒計時。

---

## 🛠️ 開發與編譯環境

如果您需要自行修改源代碼並重新打包，請參考以下環境配置：

* **開發語言**：C# 12
* **目標框架**：`.NET 8.0-windows`
* **發布命令 (框架依賴單文件)**：
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
  ```
---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---
  

## 📈 Star History

[![Star History Chart](https://api.star-history.com/svg?repos=tsf666/VirtualScreenSwitch&type=Date)](https://star-history.com/#tsf666/VirtualScreenSwitch&Date)


---


  