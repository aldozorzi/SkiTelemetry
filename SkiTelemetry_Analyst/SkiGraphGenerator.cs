using SkiTelemetry_Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class SkiGraphGenerator
{
    //ANALISI ROLL E G_FORCE
    public static void GenerateHtmlReport(List<SkiFrame> allFrames, List<SkiTurn> detectedTurns, string filePath = "SkiPhysicsAnalysis.html")
    {
        /*var leftData = allFrames.Where(p => p.Side == SensorSide.Left).ToList();
        var rightData = allPoints.Where(p => p.Side == SensorSide.Right).ToList();*/

        // Estrazione dati con cultura invariante (punto per i decimali)
        string leftRoll = string.Join(",", allFrames.Select(f => f.Left.SmoothRoll.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRoll = string.Join(",", allFrames.Select(f => f.Right.SmoothRoll.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftSmoothAccX = string.Join(",", allFrames.Select(f => f.Left.SmoothAccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightSmoothAccX = string.Join(",", allFrames.Select(f => f.Right.SmoothAccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftRawAccX = string.Join(",", allFrames.Select(f => f.Left.AccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawAccX = string.Join(",", allFrames.Select(f => f.Right.AccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftRawAccY = string.Join(",", allFrames.Select(f => f.Left.AccY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawAccY = string.Join(",", allFrames.Select(f => f.Right.AccY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftRawAccZ = string.Join(",", allFrames.Select(f => f.Left.AccZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawAccZ = string.Join(",", allFrames.Select(f => f.Right.AccZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));



        string leftG = string.Join(",", allFrames.Select(f => f.Left.AdjustedG.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightG = string.Join(",", allFrames.Select(f => f.Right.AdjustedG.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftSmoothGyroY = string.Join(",", allFrames.Select(f => f.Left.SmoothGyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightSmoothGyroY = string.Join(",", allFrames.Select(f => f.Right.SmoothGyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftSmoothGyroX = string.Join(",", allFrames.Select(f => f.Left.SmoothGyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightSmoothGyroX = string.Join(",", allFrames.Select(f => f.Right.SmoothGyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftRawGyroY = string.Join(",", allFrames.Select(f => f.Left.GyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawGyroY = string.Join(",", allFrames.Select(f => f.Right.GyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftRawGyroX = string.Join(",", allFrames.Select(f => f.Left.GyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawGyroX = string.Join(",", allFrames.Select(f => f.Right.GyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftRawGyroZ = string.Join(",", allFrames.Select(f => f.Left.GyroZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRawGyroZ = string.Join(",", allFrames.Select(f => f.Right.GyroZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string timestamps = string.Join(",", allFrames.Select(f => f.Left.Timestamp));

        var turnShapes = new StringBuilder();
        foreach (var turn in detectedTurns)
        {
            turnShapes.AppendLine($"{{ type: 'rect', xref: 'x', yref: 'paper', x0: {turn.StartTimestamp}, x1: {turn.EndTimestamp}, y0: 0, y1: 1, fillcolor: 'rgba(200, 200, 200, 0.2)', line: {{ width: 0 }} }},");
        }

        var html = $@"
    <html>
    <head><script src='https://cdn.plot.ly/plotly-latest.min.js'></script></head>
    <body>
        <div id='graph' style='width:100%; height:95vh;'></div>
        <script>
            var xData = [{timestamps}];

            // 1. ROLL (Asse Y principale - Sinistra)
            var traceRollL = {{ x: xData, y: [{leftRoll}], name: 'Roll L (deg)', line: {{color: 'rgba(255, 0, 0, 0.4)', width: 2}} }};
            var traceRollR = {{ x: xData, y: [{rightRoll}], name: 'Roll R (deg)', line: {{color: 'rgba(31, 119, 180, 0.4)', width: 2}} }};

            // 2. G-FORCE (Asse Y2 - Destra interna)
            var traceGL = {{ x: xData, y: [{leftG}], name: 'G-Force L', yaxis: 'y2', line: {{color: 'red', width: 1.5, dash: 'dot'}} }};
            var traceGR = {{ x: xData, y: [{rightG}], name: 'G-Force R', yaxis: 'y2', line: {{color: '#1f77b4', width: 1.5, dash: 'dot'}} }};
            // 2. AccX (Asse Y2 - Destra interna)
            var traceSmoothAccXL = {{ x: xData, y: [{leftSmoothAccX}], name: 'AccX Smooth L', yaxis: 'y2', line: {{color: 'green', width: 1.5, dash: 'dot'}} }};
            var traceSmoothAccXR = {{ x: xData, y: [{rightSmoothAccX}], name: 'AccX Smooth R', yaxis: 'y2', line: {{color: 'orange', width: 1.5, dash: 'dot'}} }};

            var traceRawAccXL = {{ x: xData, y: [{leftRawAccX}], name: 'AccX Raw L', yaxis: 'y2', line: {{color: 'red', width: 1.5, dash: 'dot'}} }};
            var traceRawAccXR = {{ x: xData, y: [{rightRawAccX}], name: 'AccX Raw R', yaxis: 'y2', line: {{color: 'blue', width: 1.5, dash: 'dot'}} }};
            var traceRawAccYL = {{ x: xData, y: [{leftRawAccY}], name: 'AccY Raw L', yaxis: 'y2', line: {{color: 'yellow', width: 1.5, dash: 'dot'}} }};
            var traceRawAccYR = {{ x: xData, y: [{rightRawAccY}], name: 'AccY Raw R', yaxis: 'y2', line: {{color: 'orange', width: 1.5, dash: 'dot'}} }};
            var traceRawAccZL = {{ x: xData, y: [{leftRawAccZ}], name: 'AccZ Raw L', yaxis: 'y2', line: {{color: 'purple', width: 1.5, dash: 'dot'}} }};
            var traceRawAccZR = {{ x: xData, y: [{rightRawAccZ}], name: 'AccZ Raw R', yaxis: 'y2', line: {{color: 'green', width: 1.5, dash: 'dot'}} }};

            // 3. GYRO Y (Asse Y3 - Destra esterna)
            var traceSmoothGyroYL = {{ x: xData, y: [{leftSmoothGyroY}], name: 'GyroY L (deg/s)', yaxis: 'y3', line: {{color: 'rgba(0, 128, 0, 0.6)', width: 1}} }};
            var traceSmoothGyroYR = {{ x: xData, y: [{rightSmoothGyroY}], name: 'GyroY R (deg/s)', yaxis: 'y3', line: {{color: 'rgba(139, 69, 19, 0.6)', width: 1}} }};
            var traceSmoothGyroXL = {{ x: xData, y: [{leftSmoothGyroX}], name: 'GyroZ L (deg/s)', yaxis: 'y3', line: {{color: 'red', width: 1}} }};
            var traceSmoothGyroXR = {{ x: xData, y: [{rightSmoothGyroX}], name: 'GyroZ R (deg/s)', yaxis: 'y3', line: {{color: 'blue', width: 1}} }};

            var traceRawGyroYL = {{ x: xData, y: [{leftRawGyroY}], name: 'GyroY L (deg/s)', yaxis: 'y3', line: {{color: 'red', width: 1}} }};
            var traceRawGyroYR = {{ x: xData, y: [{rightRawGyroY}], name: 'GyroY R (deg/s)', yaxis: 'y3', line: {{color: 'yellow', width: 1}} }};
            var traceRawGyroXL = {{ x: xData, y: [{leftRawGyroX}], name: 'GyroZ L (deg/s)', yaxis: 'y3', line: {{color: 'blue', width: 1}} }};
            var traceRawGyroXR = {{ x: xData, y: [{rightRawGyroX}], name: 'GyroZ R (deg/s)', yaxis: 'y3', line: {{color: 'green', width: 1}} }};
            var traceRawGyroZL = {{ x: xData, y: [{leftRawGyroZ}], name: 'GyroZ L (deg/s)', yaxis: 'y3', line: {{color: 'orange', width: 1}} }};
            var traceRawGyroZR = {{ x: xData, y: [{rightRawGyroZ}], name: 'GyroZ R (deg/s)', yaxis: 'y3', line: {{color: 'purple', width: 1}} }};

            var layout = {{
                title: 'Analisi Fisica Sci: Roll vs G-Force vs GyroY',
                xaxis: {{ title: 'Timestamp' }},
                yaxis: {{ title: 'Roll (deg)', range: [-70, 70], zeroline: true }},
                yaxis2: {{ 
                    title: 'G-Force', 
                    overlaying: 'y', 
                    side: 'right', 
                    range: [0, 4],
                    showgrid: false 
                }},
                yaxis3: {{
                    title: 'GyroY (deg/s)',
                    overlaying: 'y',
                    side: 'right',
                    anchor: 'free',
                    position: 0.95,
                    range: [-100, 100],
                    showgrid: false
                }},
                margin: {{ r: 100 }}, // Spazio per il terzo asse
                shapes: [{turnShapes}],
                hovermode: 'x unified'
            }};

            Plotly.newPlot('graph', [
                /*traceRawAccXL,
                traceRawAccXR,
                traceRawAccYL,
                traceRawAccYR,
                traceRawAccZL,
                traceRawAccZR,
                traceRawGyroYL,
                traceRawGyroYR,
                traceRawGyroXL,
                traceRawGyroXR,
                traceRawGyroZL,
                traceRawGyroZR,*/
    
                traceRollL, 
                traceRollR,
                traceSmoothAccXL,
                traceSmoothAccXR, 
                traceGL, 
                traceGR,
                traceSmoothGyroYL, 
                traceSmoothGyroYR, 
                traceSmoothGyroXL,  
                traceSmoothGyroXR
            ], layout);
        </script>
    </body>
    </html>";

        File.WriteAllText(filePath, html);
    }
}