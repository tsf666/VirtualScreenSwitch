#requires -version 3

# ====================================================================
# 部署脚本：在桌面为 VirtualScreenSwitch.exe 创建快捷方式，
# 并绑定 Ctrl + Alt + . （英文键盘句号 / 中文键盘上标">"的那个键）作为拉起热键。
#
# 说明：WshShortcut.Hotkey 这个 COM 属性，通过代码设置成句号这类符号键
# 会直接抛异常（"值不在预期的范围内"），即便手动在属性对话框里输入是可以的。
# 所以这里绕开这个属性：先正常保存快捷方式（不设置 Hotkey），
# 再直接改 .lnk 文件二进制头部里记录热键的字段（MS-SHLLINK 文档规定）：
#   偏移 64 字节 = 按键的虚拟键码
#   偏移 65 字节 = 修饰键组合（Shift=0x01，Ctrl=0x02，Alt=0x04）
# 对应 Ctrl+Alt+. ：VK_OEM_PERIOD(0xBE) + 修饰键 0x06
#
# 运行方式（必须以"文件"方式运行，不能整段复制粘贴进控制台）：
#   方式A：右键本文件 -> "使用 PowerShell 运行"
#   方式B：打开 PowerShell，cd 到本文件所在目录，输入：
#          powershell -ExecutionPolicy Bypass -File ".\deploy_hotkey_shortcut.ps1"
# ====================================================================

$ErrorActionPreference = "Stop"

try {
    Write-Host "===== 诊断信息 =====" -ForegroundColor Cyan
    Write-Host "脚本所在目录 (PSScriptRoot)：$PSScriptRoot"

    if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        throw "PSScriptRoot 为空——请用上面的方式A或方式B重新运行本脚本，而不是复制粘贴进已打开的控制台。"
    }

    # ---- 1. 定位脚本同目录下的 exe ----
    $exeName = "VirtualScreenSwitch.exe"
    $exePath = Join-Path $PSScriptRoot $exeName
    Write-Host "查找 exe 路径：$exePath"

    if (-not (Test-Path $exePath)) {
        throw "没有在脚本所在目录找到 $exeName，请把本脚本和 exe 放在同一个文件夹里（VirtualScreenSwitch\Release\）。"
    }
    Write-Host "已找到 exe。" -ForegroundColor Green

    # ---- 2. 快捷方式保存位置：桌面 ----
    $shortcutName = "VirtualScreenSwitch(with_hot_key_ctrl+alt+.).lnk"
    $desktopPath  = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath $shortcutName
    Write-Host "快捷方式将保存为：$shortcutPath"

    # ---- 3. 先正常创建快捷方式（不通过 COM 设置 Hotkey，避免报错）----
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($shortcutPath)
    $Shortcut.TargetPath       = $exePath
    $Shortcut.WorkingDirectory = $PSScriptRoot
    $Shortcut.IconLocation     = $exePath
    $Shortcut.Description      = "Ctrl+Alt+. 拉起：熄屏/锁屏小工具"
    $Shortcut.Save()

    if (-not (Test-Path $shortcutPath)) {
        throw "Save() 没有报错，但桌面上找不到生成的 .lnk 文件，请检查桌面路径/权限是否异常。"
    }
    Write-Host "快捷方式文件已创建（此时还未写入热键）。" -ForegroundColor Green

    # ---- 4. 直接改 .lnk 文件二进制头部里的热键字段 ----
    $vkOemPeriod = 0xBE   # VK_OEM_PERIOD（句号键的虚拟键码）
    $modCtrlAlt  = 0x06   # Ctrl(0x02) + Alt(0x04)

    $bytes = [System.IO.File]::ReadAllBytes($shortcutPath)
    if ($bytes.Length -lt 66) {
        throw "快捷方式文件大小异常（只有 $($bytes.Length) 字节），不能安全地写入热键字段。"
    }
    $bytes[64] = $vkOemPeriod
    $bytes[65] = $modCtrlAlt
    [System.IO.File]::WriteAllBytes($shortcutPath, $bytes)

    # ---- 5. 回读校验 ----
    $check = [System.IO.File]::ReadAllBytes($shortcutPath)
    Write-Host ("校验：byte[64]=0x{0:X2} byte[65]=0x{1:X2}" -f $check[64], $check[65])

    if ($check[64] -eq $vkOemPeriod -and $check[65] -eq $modCtrlAlt) {
        Write-Host ""
        Write-Host "成功：快捷方式已创建，热键已写入。" -ForegroundColor Green
        Write-Host "热键：Ctrl + Alt + ." -ForegroundColor Green
        Write-Host "路径：$shortcutPath" -ForegroundColor Green
    } else {
        throw "写入的字节和回读的不一致，可能有别的程序在写入后又改动了这个文件。"
    }
}
catch {
    Write-Host ""
    Write-Host "===== 出错了 =====" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    Write-Host ""
    Read-Host "按回车键关闭"
}
