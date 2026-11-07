[CmdletBinding()]
param(
    [string]$Log,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 名字来自独立审查确认的十二项契约；不能从待验日志或登记源码生成预期。
$siteCases = @(
    '站点网格：入口是原生按钮，内容由单一ContentDialog承载'
    '站点网格：三列自适应、原生整卡Toggle与横向操作条'
    '站点网格：退回竖表或固定卡宽被拒绝'
    '站点网格：局部主题保留原生模板和三种字典，无截图固定色'
    '站点网格：草稿按卡退出默认，计数与圆形标识通知，取消零保存'
    '站点网格：确认保存一次、同步宿主目录和偏好，保留已有资源'
    '站点网格：全选总是显式选择，清空不退默认且设置可确认'
    '站点网格：忙态禁操作、失败可刷新、空态不扩大显式搜索'
    '站点网格：未出现的偏好刷新不覆盖，默认切换保留手动编号'
    '站点网格：切连接、宿主重新Attach、离页使旧草稿确认失效'
    '站点网格：保存失败留下草稿和原因，不误报搜索确认'
    '站点网格：打开后外部偏好更改拒绝旧草稿覆盖'
)

function Confirm-Registration([string]$Text) {
    $lines = $Text -split '\r?\n'
    $outcomes = @($lines | ForEach-Object {
        if ($_ -match '^\s*(通过|失败|跳过)  (.+)$') {
            [pscustomobject]@{ Status = $Matches[1]; Name = $Matches[2] }
        }
    })
    foreach ($name in $siteCases) {
        $matching = @($outcomes | Where-Object { $_.Name -ceq $name })
        if ($matching.Count -ne 1 -or $matching[0].Status -cne '通过') {
            throw "站点网格检查未恰好通过一次：$name"
        }
    }
    $summaries = @($lines | Where-Object { $_ -match '^共 \d+ 项，通过 \d+，失败 0，跳过 0，用时 \d+ ms$' })
    if ($summaries.Count -ne 1 -or @($outcomes | Where-Object { $_.Status -cne '通过' }).Count -ne 0) {
        throw '必须有完整且零失败零跳过的唯一汇总，不能用部分日志证明登记通过'
    }
    $summary = [regex]::Match($summaries[0], '^共 (\d+) 项，通过 (\d+)')
    if ([int]$summary.Groups[1].Value -ne $outcomes.Count -or [int]$summary.Groups[2].Value -ne $outcomes.Count) {
        throw '汇总与实际结果数量不一致，不能接受缺失或拼接日志'
    }
}

try {
    if ($SelfTest) {
        if ($Log) { throw '-SelfTest 不接受待验日志' }
        $goodLines = @($siteCases | ForEach-Object { '  通过  ' + $_ })
        $summary = '共 12 项，通过 12，失败 0，跳过 0，用时 1 ms'
        Confirm-Registration (($goodLines + $summary) -join "`n")
        $badInputs = @(
            (($goodLines[1..11] + '共 11 项，通过 11，失败 0，跳过 0，用时 1 ms') -join "`n")
            (($goodLines + $goodLines[0] + '共 13 项，通过 13，失败 0，跳过 0，用时 1 ms') -join "`n")
            ((@('  失败  ' + $siteCases[0]) + $goodLines[1..11] + $summary) -join "`n")
            ((@('  跳过  ' + $siteCases[0]) + $goodLines[1..11] + $summary) -join "`n")
            ($goodLines -join "`n")
            (($goodLines + '共 13 项，通过 13，失败 0，跳过 0，用时 1 ms') -join "`n")
        )
        foreach ($inputText in $badInputs) {
            $rejected = $false
            try { Confirm-Registration $inputText } catch { $rejected = $true }
            if (-not $rejected) { throw '日志验证器接受了遗漏、重复、失败、跳过或不完整结果' }
        }
        Write-Output '站点登记日志验证器：完整日志通过，六种错误日志全部拒绝（7/7）'
    } else {
        if (-not $Log) { throw '请提供当轮编译并执行得到的 -Log；读取日志本身不证明当前代码已运行' }
        Confirm-Registration ([IO.File]::ReadAllText([IO.Path]::GetFullPath($Log), [Text.Encoding]::UTF8))
        Write-Output '站点网格十二项恰好通过一次，完整日志零失败零跳过'
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
