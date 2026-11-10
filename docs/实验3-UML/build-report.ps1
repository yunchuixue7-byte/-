# 生成《实验3：UML 建模》实验报告 docx（手工构造 OOXML 后打包）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = 'E:\软件\docs\实验3-UML'
$src = Join-Path $root '_docx'
$outFile = Join-Path $root '实验3-UML建模-实验报告.docx'

if (Test-Path $src) { Remove-Item -Recurse -Force $src }
New-Item -ItemType Directory -Force -Path (Join-Path $src '_rels') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $src 'word\_rels') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $src 'word\media') | Out-Null

Copy-Item (Join-Path $root '图1-主菜单与模式选择.png') (Join-Path $src 'word\media\image1.png') -Force
Copy-Item (Join-Path $root '图2-对局进行与人机应答.png') (Join-Path $src 'word\media\image2.png') -Force

$enc = New-Object System.Text.UTF8Encoding($false)
function Save-Text($path, $text) { [System.IO.File]::WriteAllText($path, $text, $enc) }

function E([string]$s) { return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;') }

function TRun([string]$t, [string]$font = '宋体', [int]$sz = 24, [bool]$b = $false) {
    $rpr = '<w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="' + $font + '"/>' + $(if ($b) { '<w:b/>' } else { '' }) + '<w:sz w:val="' + $sz + '"/><w:szCs w:val="' + $sz + '"/></w:rPr>'
    return '<w:r>' + $rpr + '<w:t xml:space="preserve">' + (E $t) + '</w:t></w:r>'
}

function P([string]$t, [string]$font = '宋体', [int]$sz = 24, [bool]$b = $false, [string]$jc = 'both', [bool]$indent = $true) {
    $ppr = '<w:pPr><w:spacing w:before="0" w:after="60" w:line="340" w:lineRule="auto"/>' + $(if ($indent) { '<w:ind w:firstLineChars="200" w:firstLine="480"/>' } else { '' }) + '<w:jc w:val="' + $jc + '"/></w:pPr>'
    return '<w:p>' + $ppr + (TRun $t $font $sz $b) + '</w:p>'
}

function H1([string]$t) {
    return '<w:p><w:pPr><w:spacing w:before="240" w:after="120" w:line="340" w:lineRule="auto"/><w:jc w:val="center"/></w:pPr>' + (TRun $t '黑体' 32 $true) + '</w:p>'
}

function H2([string]$t) {
    return '<w:p><w:pPr><w:spacing w:before="200" w:after="100" w:line="340" w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>' + (TRun $t '黑体' 28 $true) + '</w:p>'
}

function Cap([string]$t) {
    return '<w:p><w:pPr><w:spacing w:before="40" w:after="140" w:line="300" w:lineRule="auto"/><w:jc w:val="center"/></w:pPr>' + (TRun $t '楷体' 21 $false) + '</w:p>'
}

function Row($cells, [int[]]$widths, [bool]$bold, [string]$shade) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<w:tr>')
    for ($i = 0; $i -lt $cells.Count; $i++) {
        $txt = [string]$cells[$i]
        $isLabel = $txt.StartsWith('!')
        if ($isLabel) { $txt = $txt.Substring(1) }
        $cellBold = ($bold -or $isLabel)
        $fill = if ($isLabel) { 'E8EEF7' } else { $shade }
        [void]$sb.Append('<w:tc><w:tcPr><w:tcW w:w="' + $widths[$i] + '" w:type="dxa"/><w:shd w:val="clear" w:color="auto" w:fill="' + $fill + '"/><w:vAlign w:val="center"/></w:tcPr>')
        $lines = $txt -split "`n"
        foreach ($ln in $lines) {
            $ppr = '<w:pPr><w:spacing w:before="30" w:after="30" w:line="280" w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>'
            [void]$sb.Append('<w:p>' + $ppr + (TRun $ln '宋体' 21 $cellBold) + '</w:p>')
        }
        [void]$sb.Append('</w:tc>')
    }
    [void]$sb.Append('</w:tr>')
    return $sb.ToString()
}

function Tbl($header, $rows, [int[]]$widths) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<w:tbl><w:tblPr><w:tblW w:w="9000" w:type="dxa"/><w:jc w:val="center"/><w:tblBorders>')
    foreach ($side in 'top', 'left', 'bottom', 'right', 'insideH', 'insideV') {
        [void]$sb.Append('<w:' + $side + ' w:val="single" w:sz="6" w:space="0" w:color="595959"/>')
    }
    [void]$sb.Append('</w:tblBorders><w:tblLayout w:type="fixed"/></w:tblPr><w:tblGrid>')
    foreach ($w in $widths) { [void]$sb.Append('<w:gridCol w:w="' + $w + '"/>') }
    [void]$sb.Append('</w:tblGrid>')
    if ($header) { [void]$sb.Append((Row $header $widths $true 'DCE6F5')) }
    foreach ($r in $rows) { [void]$sb.Append((Row $r $widths $false 'FFFFFF')) }
    [void]$sb.Append('</w:tbl>')
    [void]$sb.Append('<w:p><w:pPr><w:spacing w:before="0" w:after="80"/></w:pPr></w:p>')
    return $sb.ToString()
}

function Img([string]$rid, [int]$cx, [int]$cy, [int]$id) {
    $drawing = '<w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="' + $cx + '" cy="' + $cy + '"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:docPr id="' + $id + '" name="图片 ' + $id + '"/><wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" noChangeAspect="1"/></wp:cNvGraphicFramePr><a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="' + $id + '" name="image' + $id + '.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="' + $rid + '"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="' + $cx + '" cy="' + $cy + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>'
    return '<w:p><w:pPr><w:spacing w:before="80" w:after="40"/><w:jc w:val="center"/></w:pPr><w:r>' + $drawing + '</w:r></w:p>'
}

# ---------- 多行长文本单元格 ----------
$uc11Main = (@(
        '1. 玩家点击棋盘上的交叉点；'
        '2. 系统校验该位置是否为空且位于 19×19 棋盘范围内；'
        '3. 校验通过后在交叉点绘制当前回合颜色的棋子并播放落子动画；'
        '4. 系统将棋子写入棋盘数据结构，并记录落子顺序（供悔棋使用）；'
        '5. 系统执行胜负判定（UC-15）；'
        '6. 若棋局未结束，切换回合（黑先白后交替），并重置 30 秒倒计时；'
        '7. 人机对战模式下由 AI 引擎执行 UC-14，完成应答落子后回到玩家回合。'
    ) -join "`n")
$uc11Alt = (@(
        '2a. 位置已被占用或越界：系统不响应，不落子、不切换回合；'
        '5a. 判定为胜或平：转入 UC-15/UC-16 结算流程，不再切换回合；'
        '6a. 倒计时归零：系统自动落子，不判负（超时自动落子）；若棋盘已满则判平。'
    ) -join "`n")
$uc12Main = (@(
        '1. 玩家点击“悔棋”；'
        '2. 系统判断当前对局模式：本地双人模式撤销最近一步；人机模式连续撤销两步（AI 一步、玩家一步）；'
        '3. 系统按落子顺序移除相应棋子，恢复棋盘状态并翻转回合；'
        '4. 人机模式下撤销后仍为玩家回合，系统重置 30 秒倒计时。'
    ) -join "`n")
$uc12Alt = (@(
        '2a. 棋盘上没有可撤销的棋子：按钮无效或提示“暂无棋子可悔”；'
        '3a. 对局已结束（终局）：允许悔棋，撤销后棋局回到进行中状态。'
    ) -join "`n")
$uc14Main = (@(
        '1. 对局管理模块通知 AI 引擎开始计算；'
        '2. AI 引擎依据当前难度档位（九档，默认“普通”）确定评估强度；'
        '3. 遍历棋盘空位进行棋型评估（连子、活三、冲四等），分别记录己方最优落点与对方威胁点；'
        '4. 按难度加权选出最佳落点，将结果返回对局管理模块；'
        '5. 系统在选定交叉点落子并播放落子动画，随后执行胜负判定（UC-15）；'
        '6. 回合切换回玩家，重置 30 秒倒计时。'
    ) -join "`n")
$uc14Alt = (@(
        '3a. 棋盘已无空位：直接判平（SF-02），不再计算；'
        '5a. 计算耗时接近倒计时上限：返回当前最优解，保证不触发超时（AI-02）。'
    ) -join "`n")
$uc15Main = (@(
        '1. 系统以最后一颗棋子为中心，检查横、竖、两条斜线共四个方向的连续同色棋子数；'
        '2. 任一方向达到五连，判定该方获胜；'
        '3. 记录获胜的五颗棋子位置并触发结算：棋盘压暗、获胜五子逐个高亮放大、金色流光连线、结算文字贴图按序出现（UC-16）；'
        '4. 棋局置为结束状态，停止倒计时；终局后仍可悔棋（UC-12）或重开新局（UC-10）。'
    ) -join "`n")
$uc15Alt = (@(
        '2a. 无五连且棋盘已满：判定平局，展示平局结算；'
        '2b. 无五连且仍有空位：对局继续，返回 UC-11 第 6 步切换回合。'
    ) -join "`n")

# ================= 正文 =================
$b = ''
$b += H1 '实验3：UML 建模'

$b += Tbl $null @(
    @('!课程名称', '《软件工程实验》', '!成绩评定', '')
    @('!实验项目名称', '《UML 建模》', '!指导教师', '甘文生')
    @('!实验项目编号', '3', '!实验项目类型', '综合实验')
    @('!实验地点', 'N515', '!学院', '网络空间安全学院')
    @('!专业', '密码科学与技术', '!学生姓名', '齐唐乐')
    @('!学号', '2023104553', '!实验时间', '2026 年 10 月 20 日')
) @(1500, 3200, 1500, 2800)

$b += H2 '一、实验目的'
$b += P '掌握面向对象分析与设计方法，学会使用 UML 用例图描述系统需求。基于实验2 交付的《趣味五子棋需求规格说明书》，识别系统参与者与用例、明确系统边界，并用规范的 UML 关系（关联、包含、扩展、泛化）描述用例之间的结构，为后续的概要设计（实验4）与详细设计（实验5）建立模型基础。'

$b += H2 '二、实验环境与工具'
$b += P '操作系统：Windows 11。'
$b += P 'UML 建模工具：draw.io（diagrams.net）UML 用例图模板，导出 PNG 图片。'
$b += P '文档工具：WPS Office。'
$b += P '建模输入：实验2 交付的《趣味五子棋需求规格说明书》第 8 章功能需求清单及模块缩写表（ZC 主菜单 / DJ 对局管理 / RJ 人机对战 / SF 胜负判定 / HQ 悔棋 / JS 计时 / XS 显示与全屏 / AI 引擎）。'

$b += H2 '三、实验内容与步骤'
$b += P '1. 熟悉 UML 建模工具：打开 draw.io，练习用例图基本元素（参与者、用例、系统边界、关联、包含、扩展、泛化）的绘制与导出。'
$b += P '2. 确定系统边界与参与者：系统边界为“趣味五子棋系统”；主要参与者为玩家，辅助参与者为 AI 引擎（人机对战模式下由系统调用的自动落子引擎）。'
$b += P '3. 提取用例并建立追溯关系：按主菜单、对局管理、人机对战、胜负判定、计时、显示与全屏六个功能模块提取用例，为每个用例标注对应的需求编号。'
$b += P '4. 绘制用例图：绘制“主菜单与模式选择”和“对局进行与人机应答”两张用例图，完整标注参与者、用例及其关系。'
$b += P '5. 编写用例描述：对落子、悔棋、AI 回合响应、胜负判定与结算等核心用例，按前置条件、主流程、备选流程、后置条件编写用例规约。'

$b += H2 '四、参与者与用例识别'
$b += P '4.1 参与者'
$b += Tbl @('参与者', '类型', '职责说明') @(
    @('玩家', '主要参与者', '启动游戏、选择游戏模式与 AI 难度、选择棋盘皮肤、切换全屏、落子、悔棋、返回主菜单、退出游戏等操作的执行者。')
    @('AI 引擎', '辅助参与者', '人机对战模式下被系统调用的自动落子引擎，独立完成棋型评估与最佳落点选择，并与对局管理模块交互。')
) @(1400, 1600, 6000)

$b += P '4.2 用例清单与需求对应关系'
$b += Tbl @('用例编号', '用例名称', '所属模块', '对应需求编号', '所在用例图') @(
    @('UC-01', '启动游戏', '主菜单', 'ZC-01', '图1')
    @('UC-02', '选择游戏模式', '主菜单', 'ZC-02', '图1')
    @('UC-03', '人机对战', '人机对战', 'RJ-01', '图1')
    @('UC-04', '本地双人对战', '主菜单 / 对局管理', 'ZC-02', '图1')
    @('UC-05', '联机对战（占位）', '主菜单', 'ZC-02', '图1')
    @('UC-06', '设置 AI 难度', '人机对战', 'RJ-02', '图1')
    @('UC-07', '选择棋盘皮肤', '显示与全屏', 'XS-01', '图1')
    @('UC-08', '切换全屏', '显示与全屏', 'XS-02、XS-03', '图1')
    @('UC-09', '退出游戏', '对局管理', 'DJ-06', '图1')
    @('UC-10', '开始新对局', '对局管理', 'DJ-04', '图2')
    @('UC-11', '落子', '对局管理', 'DJ-01、DJ-02', '图2')
    @('UC-12', '悔棋', '对局管理', 'DJ-03', '图2')
    @('UC-13', '返回主菜单', '对局管理', 'DJ-05', '图2')
    @('UC-14', 'AI 计算最佳落点', 'AI 引擎', 'AI-01、AI-02、RJ-01', '图2')
    @('UC-15', '胜负判定', '胜负判定', 'SF-01、SF-02', '图2')
    @('UC-16', '结算动画展示', '胜负判定 / 显示', 'SF-03、XS-05', '图2')
    @('UC-17', '30 秒倒计时（超时自动落子）', '计时', 'JS-01、JS-02', '图2')
) @(1100, 2600, 2000, 2300, 1000)

$b += H2 '五、用例图'
$b += P '图1 表示玩家与主菜单相关用例的关联，以及“选择游戏模式”与三种对局模式之间的泛化关系（人机对战、本地双人对战、联机对战都是选择游戏模式的一种）、设置 AI 难度对“人机对战”的扩展关系。'
$b += Img 'rId2' 5600000 4008421 1
$b += Cap '图1  主菜单与模式选择用例图'
$b += P '图2 表示对局进行阶段的用例与参与者。其中“落子”包含“胜负判定”与“30 秒倒计时（超时自动落子）”，“结算动画展示”扩展“胜负判定”；玩家参与开始新对局、落子、悔棋、返回主菜单，AI 引擎作为辅助参与者只参与“AI 计算最佳落点”。'
$b += Img 'rId3' 5600000 3715122 2
$b += Cap '图2  对局进行与人机应答用例图'

$b += H2 '六、用例描述'
$b += P '6.1 UC-11 落子'
$b += Tbl @('项目', '内容') @(
    @('!用例编号', 'UC-11')
    @('!用例名称', '落子')
    @('!参与者', '玩家（主要）；AI 引擎（辅助，人机模式下由系统代为落子）')
    @('!对应需求', 'DJ-01 落子、DJ-02 回合切换；JS-01 回合倒计时、JS-02 超时自动落子')
    @('!前置条件', '已进入对局界面；当前回合属于落子方；棋局未结束')
    @('!触发条件', '玩家点击棋盘上的交叉点；或当前回合倒计时归零')
    @('!主流程', $uc11Main)
    @('!备选流程', $uc11Alt)
    @('!后置条件', '棋盘与回合状态更新，倒计时重置；若棋局结束则显示胜负或平局结算。')
    @('!业务规则', '黑方先行；同一方不可连续落子；棋子落下后不可移动。')
) @(1400, 7600)

$b += P '6.2 UC-12 悔棋'
$b += Tbl @('项目', '内容') @(
    @('!用例编号', 'UC-12')
    @('!用例名称', '悔棋')
    @('!参与者', '玩家')
    @('!对应需求', 'DJ-03 悔棋')
    @('!前置条件', '已进入对局；本地双人模式至少已落 1 子，人机模式至少已落 2 子')
    @('!触发条件', '玩家点击“悔棋”按钮')
    @('!主流程', $uc12Main)
    @('!备选流程', $uc12Alt)
    @('!后置条件', '棋盘恢复到悔棋前一步（双人）或两步（人机）的状态，回合与倒计时同步恢复。')
) @(1400, 7600)

$b += P '6.3 UC-14 AI 回合响应（AI 计算最佳落点）'
$b += Tbl @('项目', '内容') @(
    @('!用例编号', 'UC-14')
    @('!用例名称', 'AI 计算最佳落点（AI 回合响应）')
    @('!参与者', 'AI 引擎（辅助参与者）；对局管理模块')
    @('!对应需求', 'AI-01 AI 棋型评估、AI-02 AI 回合响应；RJ-01 人机对战模式')
    @('!前置条件', '当前为人机对战模式；轮到 AI 回合；棋局未结束')
    @('!触发条件', '玩家落子完成并切换回合；或对局开始（AI 执白后手）')
    @('!主流程', $uc14Main)
    @('!备选流程', $uc14Alt)
    @('!后置条件', 'AI 完成一次落子，棋盘状态更新，回合回到玩家。')
    @('!业务规则', 'AI 决策时间应短于 30 秒倒计时；难度越高评估越深、强度越大。')
) @(1400, 7600)

$b += P '6.4 UC-15 胜负判定（含 UC-16 结算动画展示）'
$b += Tbl @('项目', '内容') @(
    @('!用例编号', 'UC-15（含 UC-16）')
    @('!用例名称', '胜负判定与结算动画展示')
    @('!参与者', '对局管理模块（系统）；玩家（观察结算结果）')
    @('!对应需求', 'SF-01 五连判定、SF-02 平局判定、SF-03 胜利结算动画；XS-05 落子动画')
    @('!前置条件', '每次落子完成后自动触发')
    @('!触发条件', '任一棋子落下后')
    @('!主流程', $uc15Main)
    @('!备选流程', $uc15Alt)
    @('!后置条件', '棋局结束，界面显示获胜方或平局结果。')
) @(1400, 7600)

$b += H2 '七、实验小结'
$b += P '本实验以实验2 的需求规格说明书为输入，完成了参与者识别、用例提取、系统边界确定、两张用例图的绘制以及四个核心用例的规约描述。为每个用例标注了需求编号（ZC/DJ/RJ/SF/JS/XS/AI），保证了从需求到模型的可追溯性。'
$b += P '从用例模型可以看到：玩家是唯一的主要参与者；AI 引擎作为辅助参与者，只参与人机对战中的自动应答；“选择游戏模式”通过泛化统一了三种对局入口；“落子”包含“胜负判定”与“30 秒倒计时”，而“结算动画展示”只在终局发生，因此用扩展关系表达。该模型与现有代码结构一致：GameController 负责回合与流程控制，GameRules 负责五连与平局判定，AiEngine 负责棋型评估与选点。'
$b += P '实验中体会到，用例图重在表达“谁做什么”，不应混入类结构、接口与时序细节；这些内容属于概要设计与详细设计阶段（实验4、实验5）的工作。'

$docXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><w:body>' +
$b +
'<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="851" w:footer="992" w:gutter="0"/></w:sectPr>' +
'</w:body></w:document>'

$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>'

$rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'

$docRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image2.png"/></Relationships>'

$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="宋体"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:line="340" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="28"/></w:rPr></w:style></w:styles>'

Save-Text (Join-Path $src '[Content_Types].xml') $contentTypes
Save-Text (Join-Path $src '_rels\.rels') $rels
Save-Text (Join-Path $src 'word\document.xml') $docXml
Save-Text (Join-Path $src 'word\_rels\document.xml.rels') $docRels
Save-Text (Join-Path $src 'word\styles.xml') $styles

# 打包前校验各 XML 部件
foreach ($part in @('[Content_Types].xml', '_rels\.rels', 'word\document.xml', 'word\_rels\document.xml.rels', 'word\styles.xml')) {
    $full = Join-Path $src $part
    $null = [xml]([System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8))
}

if (Test-Path $outFile) { Remove-Item -Force $outFile }

# 显式写入 zip 条目，确保分隔符为 '/'（ZIP/OPC 规范要求）
$parts = @(
    '[Content_Types].xml'
    '_rels/.rels'
    'word/document.xml'
    'word/styles.xml'
    'word/media/image1.png'
    'word/media/image2.png'
    'word/_rels/document.xml.rels'
)
$fs = [System.IO.File]::Open($outFile, [System.IO.FileMode]::Create)
$archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($p in $parts) {
    $entry = $archive.CreateEntry($p, [System.IO.Compression.CompressionLevel]::Optimal)
    $es = $entry.Open()
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $src ($p -replace '/', '\')))
    $es.Write($bytes, 0, $bytes.Length)
    $es.Close()
}
$archive.Dispose()
$fs.Close()

# 打包后回读校验
$zip = [System.IO.Compression.ZipFile]::OpenRead($outFile)
$names = @($zip.Entries | ForEach-Object { $_.FullName })
foreach ($e in $zip.Entries | Where-Object { $_.FullName -like '*.xml' -or $_.FullName -like '*.rels' }) {
    $sr = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
    $null = [xml]$sr.ReadToEnd()
    $sr.Close()
}
$zip.Dispose()
'docx 部件：' + ($names -join ', ')
'大小: ' + (Get-Item $outFile).Length + ' 字节'
