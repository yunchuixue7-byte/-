# 生成实验3 用例图：PNG（插入报告用）+ drawio（可编辑 UML 源文件）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = 'E:\软件\docs\实验3-UML'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$C = @{
    Line    = [System.Drawing.Color]::FromArgb(48, 58, 72)
    UcFill  = [System.Drawing.Color]::FromArgb(232, 241, 252)
    UcLine  = [System.Drawing.Color]::FromArgb(64, 116, 178)
    BndLine = [System.Drawing.Color]::FromArgb(146, 158, 174)
    Text    = [System.Drawing.Color]::FromArgb(24, 30, 40)
    Title   = [System.Drawing.Color]::FromArgb(18, 42, 84)
}

function New-PenR([System.Drawing.Color]$c, [single]$w, [bool]$dash) {
    $p = New-Object System.Drawing.Pen($c, $w)
    if ($dash) { $p.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash }
    return $p
}

function New-Pt([single]$x, [single]$y) { return [System.Drawing.PointF]::new($x, $y) }

function Get-ShapeAnchor($shape, [System.Drawing.PointF]$towards) {
    $dx = $towards.X - $shape.X
    $dy = $towards.Y - $shape.Y
    if ($dx -eq 0 -and $dy -eq 0) { return (New-Pt $shape.X $shape.Y) }
    if ($shape.Kind -eq 'uc') {
        $a = [double]$shape.W / 2.0
        $b = [double]$shape.H / 2.0
        $t = 1.0 / [Math]::Sqrt(($dx / $a) * ($dx / $a) + ($dy / $b) * ($dy / $b))
        return (New-Pt ([single]($shape.X + $dx * $t)) ([single]($shape.Y + $dy * $t)))
    }
    else {
        $hw = 64.0; $hh = 80.0
        $tx = if ($dx -ne 0) { $hw / [Math]::Abs($dx) } else { [double]::MaxValue }
        $ty = if ($dy -ne 0) { $hh / [Math]::Abs($dy) } else { [double]::MaxValue }
        $t = [Math]::Min($tx, $ty)
        return (New-Pt ([single]($shape.X + $dx * $t)) ([single]($shape.Y + $dy * $t)))
    }
}

function Draw-Actor($g, $shape, $font, $pen) {
    $x = $shape.X; $y = $shape.Y
    $g.DrawEllipse($pen, ($x - 22), ($y - 74), 44, 44)
    $g.DrawLine($pen, $x, ($y - 30), $x, ($y + 22))
    $g.DrawLine($pen, ($x - 40), ($y - 10), ($x + 40), ($y - 10))
    $g.DrawLine($pen, $x, ($y + 22), ($x - 30), ($y + 68))
    $g.DrawLine($pen, $x, ($y + 22), ($x + 30), ($y + 68))
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = [System.Drawing.RectangleF]::new(($x - 120), ($y + 76), 240, 44)
    $g.DrawString($shape.Label, $font, $script:brText, $rect, $fmt)
}

function Draw-UseCase($g, $shape, $font, $pen) {
    $rect = [System.Drawing.RectangleF]::new(($shape.X - $shape.W / 2), ($shape.Y - $shape.H / 2), $shape.W, $shape.H)
    $g.FillEllipse($script:brUcFill, $rect)
    $g.DrawEllipse($pen, $rect)
    $inner = [System.Drawing.RectangleF]::new(($rect.X + 24), ($rect.Y + 12), ($rect.Width - 48), ($rect.Height - 24))
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($shape.Label, $font, $script:brText, $inner, $fmt)
}

function Get-EdgePath($edge, $shapes) {
    $from = $shapes[$edge.From]
    $to = $shapes[$edge.To]
    $wps = @()
    if ($edge.ContainsKey('Waypoints')) {
        foreach ($p in $edge.Waypoints) { $wps += , (New-Pt ([single]$p[0]) ([single]$p[1])) }
    }
    $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    $firstRef = if ($wps.Count -gt 0) { $wps[0] } else { New-Pt ([single]$to.X) ([single]$to.Y) }
    $pts.Add((Get-ShapeAnchor $from $firstRef))
    foreach ($w in $wps) { $pts.Add($w) }
    $lastRef = if ($wps.Count -gt 0) { $wps[$wps.Count - 1] } else { $pts[0] }
    $pts.Add((Get-ShapeAnchor $to $lastRef))
    return $pts
}

function Draw-Edge($g, $pts, $edge, $fontLabel, $penSolid, $penDash) {
    $kind = $edge.Kind
    $pen = if ($kind -eq 'assoc' -or $kind -eq 'gen') { $penSolid } else { $penDash }
    for ($i = 0; $i -lt $pts.Count - 1; $i++) {
        $g.DrawLine($pen, $pts[$i].X, $pts[$i].Y, $pts[$i + 1].X, $pts[$i + 1].Y)
    }
    $end = $pts[$pts.Count - 1]
    $prev = $pts[$pts.Count - 2]
    $ang = [Math]::Atan2([double]($end.Y - $prev.Y), [double]($end.X - $prev.X))
    if ($kind -eq 'gen') {
        $s = 22.0; $sp = 0.40
        $p1 = New-Pt ([single]($end.X - $s * [Math]::Cos($ang - $sp))) ([single]($end.Y - $s * [Math]::Sin($ang - $sp)))
        $p2 = New-Pt ([single]($end.X - $s * [Math]::Cos($ang + $sp))) ([single]($end.Y - $s * [Math]::Sin($ang + $sp)))
        $tri = [System.Drawing.PointF[]]@($end, $p1, $p2)
        $g.FillPolygon([System.Drawing.Brushes]::White, $tri)
        $g.DrawPolygon($penSolid, $tri)
    }
    elseif ($kind -eq 'include' -or $kind -eq 'extend') {
        $s = 24.0; $sp = 0.45
        $p1 = New-Pt ([single]($end.X - $s * [Math]::Cos($ang - $sp))) ([single]($end.Y - $s * [Math]::Sin($ang - $sp)))
        $p2 = New-Pt ([single]($end.X - $s * [Math]::Cos($ang + $sp))) ([single]($end.Y - $s * [Math]::Sin($ang + $sp)))
        $g.DrawLine($pen, $end.X, $end.Y, $p1.X, $p1.Y)
        $g.DrawLine($pen, $end.X, $end.Y, $p2.X, $p2.Y)
    }
    if ($edge.ContainsKey('Label') -and $edge.Label) {
        $mid = [int][Math]::Floor(($pts.Count - 1) / 2)
        $a = $pts[$mid]; $b = $pts[$mid + 1]
        $mx = ($a.X + $b.X) / 2.0; $my = ($a.Y + $b.Y) / 2.0
        $segAng = [Math]::Atan2([double]($b.Y - $a.Y), [double]($b.X - $a.X))
        $off = 26.0
        $lx = [single]($mx - $off * [Math]::Sin($segAng))
        $ly = [single]($my + $off * [Math]::Cos($segAng))
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = [System.Drawing.StringAlignment]::Center
        $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = [System.Drawing.RectangleF]::new(($lx - 100), ($ly - 20), 200, 40)
        $g.FillRectangle([System.Drawing.Brushes]::White, $rect)
        $g.DrawString($edge.Label, $fontLabel, $script:brText, $rect, $fmt)
    }
}

function Render-Diagram($spec, $pngPath) {
    $script:brText = New-Object System.Drawing.SolidBrush($C.Text)
    $script:brUcFill = New-Object System.Drawing.SolidBrush($C.UcFill)
    $bm = New-Object System.Drawing.Bitmap($spec.Width, $spec.Height)
    $bm.SetResolution(144, 144)
    $g = [System.Drawing.Graphics]::FromImage($bm)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.Clear([System.Drawing.Color]::White)

    $penSolid = New-PenR $C.Line 2.2 $false
    $penDash = New-PenR $C.Line 1.8 $true
    $penUc = New-PenR $C.UcLine 2.2 $false
    $penBnd = New-PenR $C.BndLine 2.0 $false
    $fontTitle = New-Object System.Drawing.Font('Microsoft YaHei', 27, [System.Drawing.FontStyle]::Bold)
    $fontUc = New-Object System.Drawing.Font('Microsoft YaHei', 20)
    $fontActor = New-Object System.Drawing.Font('Microsoft YaHei', 21, [System.Drawing.FontStyle]::Bold)
    $fontLabel = New-Object System.Drawing.Font('Microsoft YaHei', 17, [System.Drawing.FontStyle]::Italic)
    $fontBnd = New-Object System.Drawing.Font('Microsoft YaHei', 20, [System.Drawing.FontStyle]::Bold)

    $g.DrawString($spec.Title, $fontTitle, (New-Object System.Drawing.SolidBrush($C.Title)), 40, 22)

    $b = $spec.Boundary
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(250, 251, 253))), $b.X, $b.Y, $b.W, $b.H)
    $g.DrawRectangle($penBnd, $b.X, $b.Y, $b.W, $b.H)
    $fmtB = New-Object System.Drawing.StringFormat
    $fmtB.Alignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($b.Label, $fontBnd, (New-Object System.Drawing.SolidBrush($C.Text)), ([System.Drawing.RectangleF]::new($b.X, ($b.Y + 10), $b.W, 40)), $fmtB)

    $shapes = @{}
    foreach ($a in $spec.Actors) { $shapes[$a.Id] = @{ Kind = 'actor'; Id = $a.Id; Label = $a.Label; X = [double]$a.X; Y = [double]$a.Y } }
    foreach ($u in $spec.UseCases) { $shapes[$u.Id] = @{ Kind = 'uc'; Id = $u.Id; Label = $u.Label; X = [double]$u.X; Y = [double]$u.Y; W = [double]$u.W; H = [double]$u.H } }

    foreach ($e in $spec.Edges) {
        Draw-Edge $g (Get-EdgePath $e $shapes) $e $fontLabel $penSolid $penDash
    }
    foreach ($u in $spec.UseCases) { Draw-UseCase $g $shapes[$u.Id] $fontUc $penUc }
    foreach ($a in $spec.Actors) { Draw-Actor $g $shapes[$a.Id] $fontActor $penSolid }

    $bm.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bm.Dispose()
    $script:brText.Dispose(); $script:brUcFill.Dispose()
}

function Esc([string]$s) { return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;') }

function Render-DrawioPage($spec, $pageName) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('  <diagram id="' + ($pageName -replace '[^0-9A-Za-z]', '') + '" name="' + (Esc $pageName) + '">')
    [void]$sb.AppendLine('    <mxGraphModel dx="1600" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="' + $spec.Width + '" pageHeight="' + $spec.Height + '" math="0" shadow="0">')
    [void]$sb.AppendLine('      <root>')
    [void]$sb.AppendLine('        <mxCell id="0" /><mxCell id="1" parent="0" />')
    $b = $spec.Boundary
    [void]$sb.AppendLine('        <mxCell id="bnd" value="' + (Esc $b.Label) + '" style="rounded=0;whiteSpace=wrap;html=1;fillColor=#FAFBFD;strokeColor=#929EAE;verticalAlign=top;fontSize=18;fontStyle=1;fontFamily=Microsoft YaHei;" vertex="1" parent="1"><mxGeometry x="' + $b.X + '" y="' + $b.Y + '" width="' + $b.W + '" height="' + $b.H + '" as="geometry" /></mxCell>')
    [void]$sb.AppendLine('        <mxCell id="ttl" value="' + (Esc $spec.Title) + '" style="text;html=1;fontSize=24;fontStyle=1;align=left;verticalAlign=middle;fontFamily=Microsoft YaHei;" vertex="1" parent="1"><mxGeometry x="40" y="20" width="820" height="50" as="geometry" /></mxCell>')
    foreach ($a in $spec.Actors) {
        [void]$sb.AppendLine('        <mxCell id="' + $a.Id + '" value="' + (Esc $a.Label) + '" style="shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;fontSize=18;fontStyle=1;fontFamily=Microsoft YaHei;" vertex="1" parent="1"><mxGeometry x="' + ([int]$a.X - 22) + '" y="' + ([int]$a.Y - 74) + '" width="44" height="88" as="geometry" /></mxCell>')
    }
    foreach ($u in $spec.UseCases) {
        $txt = (Esc $u.Label).Replace("`n", '&lt;br&gt;')
        [void]$sb.AppendLine('        <mxCell id="' + $u.Id + '" value="' + $txt + '" style="ellipse;whiteSpace=wrap;html=1;fillColor=#E8F1FC;strokeColor=#4074B2;fontSize=17;fontFamily=Microsoft YaHei;" vertex="1" parent="1"><mxGeometry x="' + ([int]($u.X - $u.W / 2)) + '" y="' + ([int]($u.Y - $u.H / 2)) + '" width="' + $u.W + '" height="' + $u.H + '" as="geometry" /></mxCell>')
    }
    $n = 0
    foreach ($e in $spec.Edges) {
        $n++
        $style = switch ($e.Kind) {
            'gen' { 'endArrow=block;endFill=0;html=1;strokeWidth=2;fontFamily=Microsoft YaHei;fontSize=15;' }
            'include' { 'endArrow=open;dashed=1;html=1;strokeWidth=2;endSize=10;fontStyle=2;fontFamily=Microsoft YaHei;fontSize=15;' }
            'extend' { 'endArrow=open;dashed=1;html=1;strokeWidth=2;endSize=10;fontStyle=2;fontFamily=Microsoft YaHei;fontSize=15;' }
            default { 'endArrow=none;html=1;strokeWidth=2;' }
        }
        $lbl = if ($e.ContainsKey('Label') -and $e.Label) { ' value="' + (Esc $e.Label) + '"' } else { '' }
        [void]$sb.AppendLine('        <mxCell id="e' + $n + '"' + $lbl + ' style="' + $style + '" edge="1" parent="1" source="' + $e.From + '" target="' + $e.To + '">')
        if ($e.ContainsKey('Waypoints')) {
            [void]$sb.AppendLine('          <mxGeometry relative="1" as="geometry"><Array as="points">')
            foreach ($p in $e.Waypoints) { [void]$sb.AppendLine('            <mxPoint x="' + $p[0] + '" y="' + $p[1] + '" />') }
            [void]$sb.AppendLine('          </Array></mxGeometry>')
        }
        else {
            [void]$sb.AppendLine('          <mxGeometry relative="1" as="geometry" />')
        }
        [void]$sb.AppendLine('        </mxCell>')
    }
    [void]$sb.AppendLine('      </root>')
    [void]$sb.AppendLine('    </mxGraphModel>')
    [void]$sb.AppendLine('  </diagram>')
    return $sb.ToString()
}

# ===================== 图1：主菜单与模式选择 =====================
$spec1 = @{
    Title    = '图1  主菜单与模式选择用例图'
    Width    = 1900
    Height   = 1360
    Boundary = @{ X = 390; Y = 100; W = 1400; H = 1180; Label = '趣味五子棋系统' }
    Actors   = @(
        @{ Id = 'player'; Label = '玩  家'; X = 170; Y = 680 }
    )
    UseCases = @(
        @{ Id = 'start'; Label = '启动游戏'; X = 780; Y = 240; W = 320; H = 110 }
        @{ Id = 'mode'; Label = '选择游戏模式'; X = 780; Y = 480; W = 320; H = 110 }
        @{ Id = 'diff'; Label = '设置 AI 难度'; X = 780; Y = 720; W = 320; H = 110 }
        @{ Id = 'skin'; Label = '选择棋盘皮肤'; X = 780; Y = 960; W = 320; H = 110 }
        @{ Id = 'quit'; Label = '退出游戏'; X = 780; Y = 1200; W = 320; H = 110 }
        @{ Id = 'pve'; Label = '人机对战'; X = 1450; Y = 340; W = 320; H = 110 }
        @{ Id = 'pvp'; Label = '本地双人对战'; X = 1450; Y = 580; W = 320; H = 110 }
        @{ Id = 'online'; Label = '联机对战'; X = 1450; Y = 820; W = 320; H = 110 }
        @{ Id = 'full'; Label = '切换全屏'; X = 1450; Y = 1060; W = 320; H = 110 }
    )
    Edges    = @(
        @{ From = 'player'; To = 'start'; Kind = 'assoc' }
        @{ From = 'player'; To = 'mode'; Kind = 'assoc' }
        @{ From = 'player'; To = 'diff'; Kind = 'assoc' }
        @{ From = 'player'; To = 'skin'; Kind = 'assoc' }
        @{ From = 'player'; To = 'quit'; Kind = 'assoc' }
        @{ From = 'player'; To = 'full'; Kind = 'assoc'; Waypoints = @(@(170, 1320), @(1450, 1320)) }
        @{ From = 'pve'; To = 'mode'; Kind = 'gen' }
        @{ From = 'pvp'; To = 'mode'; Kind = 'gen' }
        @{ From = 'online'; To = 'mode'; Kind = 'gen' }
        @{ From = 'diff'; To = 'pve'; Kind = 'extend'; Label = '«extend»' }
    )
}

# ===================== 图2：对局进行与人机应答 =====================
$spec2 = @{
    Title    = '图2  对局进行与人机应答用例图'
    Width    = 2050
    Height   = 1360
    Boundary = @{ X = 390; Y = 100; W = 1400; H = 1180; Label = '趣味五子棋系统' }
    Actors   = @(
        @{ Id = 'player'; Label = '玩  家'; X = 170; Y = 620 }
        @{ Id = 'ai'; Label = 'AI 引擎'; X = 1930; Y = 300 }
    )
    UseCases = @(
        @{ Id = 'start'; Label = '开始新对局'; X = 780; Y = 240; W = 320; H = 110 }
        @{ Id = 'move'; Label = '落子'; X = 780; Y = 580; W = 320; H = 110 }
        @{ Id = 'undo'; Label = '悔棋'; X = 780; Y = 900; W = 320; H = 110 }
        @{ Id = 'back'; Label = '返回主菜单'; X = 780; Y = 1180; W = 320; H = 110 }
        @{ Id = 'aiuc'; Label = 'AI 计算最佳落点'; X = 1450; Y = 240; W = 460; H = 110 }
        @{ Id = 'judge'; Label = '胜负判定'; X = 1450; Y = 580; W = 320; H = 110 }
        @{ Id = 'settle'; Label = '结算动画展示'; X = 1450; Y = 880; W = 340; H = 110 }
        @{ Id = 'timer'; Label = "30 秒倒计时`n（超时自动落子）"; X = 1450; Y = 1160; W = 420; H = 150 }
    )
    Edges    = @(
        @{ From = 'player'; To = 'start'; Kind = 'assoc' }
        @{ From = 'player'; To = 'move'; Kind = 'assoc' }
        @{ From = 'player'; To = 'undo'; Kind = 'assoc' }
        @{ From = 'player'; To = 'back'; Kind = 'assoc' }
        @{ From = 'ai'; To = 'aiuc'; Kind = 'assoc' }
        @{ From = 'move'; To = 'judge'; Kind = 'include'; Label = '«include»' }
        @{ From = 'move'; To = 'timer'; Kind = 'include'; Label = '«include»' }
        @{ From = 'settle'; To = 'judge'; Kind = 'extend'; Label = '«extend»' }
    )
}

Render-Diagram $spec1 (Join-Path $outDir '图1-主菜单与模式选择.png')
Render-Diagram $spec2 (Join-Path $outDir '图2-对局进行与人机应答.png')

$drawio = New-Object System.Text.StringBuilder
[void]$drawio.AppendLine('<mxfile host="app.diagrams.net" agent="Gomoku-SE-Lab" version="21.6.8">')
[void]$drawio.Append((Render-DrawioPage $spec1 '图1 主菜单与模式选择'))
[void]$drawio.Append((Render-DrawioPage $spec2 '图2 对局进行与人机应答'))
[void]$drawio.AppendLine('</mxfile>')
$drawioPath = Join-Path $outDir '五子棋用例图.drawio'
[System.IO.File]::WriteAllText($drawioPath, $drawio.ToString(), (New-Object System.Text.UTF8Encoding($false)))

# 校验 drawio 是否为格式良好的 XML（属性值里的裸 '<' 会让 draw.io 报“非绘图文件”）
$null = [xml]([System.IO.File]::ReadAllText($drawioPath, [System.Text.Encoding]::UTF8))
'drawio XML 校验通过'

Get-ChildItem $outDir | Select-Object Name, Length
