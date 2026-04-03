using SkiTelemetry_Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class SkiGraphGenerator
{
    // DETECTING CURVE
    /*public static void GenerateHtmlReport(List<SkiDataPoint> allPoints, List<SkiTurn> detectedTurns, string filePath = "SkiDebugGraph.html")
    {
        // Sincronizziamo i dati per timestamp per calcolare la media reale (CombinedGyro)
        var combinedData = allPoints
            .GroupBy(p => p.Timestamp)
            .Select(g => new {
                Ts = g.Key,
                // Media tra Left e Right (se mancano dati per un lato, usa 0 o l'unico presente)
                Val = g.Average(p => p.SmoothGyroZ)
            })
            .OrderBy(x => x.Ts)
            .ToList();

        string combinedX = string.Join(",", combinedData.Select(d => d.Ts));
        string combinedY = string.Join(",", combinedData.Select(d => d.Val.ToString("F2").Replace(",", ".")));

        // Rettangoli per le curve (Zone Rosse)
        var turnShapes = new StringBuilder();
        foreach (var turn in detectedTurns)
        {
            turnShapes.AppendLine($@"{{
            type: 'rect', xref: 'x', yref: 'paper',
            x0: {turn.StartTimestamp}, x1: {turn.EndTimestamp},
            y0: 0, y1: 1,
            fillcolor: 'rgba(255, 0, 0, 0.2)',
            line: {{ width: 1 }}
        }},");
        }

        var sb = new StringBuilder();
        sb.AppendLine($@"
    <html>
    <head>
        <script src='https://cdn.plot.ly/plotly-latest.min.js'></script>
    </head>
    <body>
        <div id='graph' style='width:100%; height:95vh;'></div>
        <script>
            var traceCombined = {{
                x: [{combinedX}],
                y: [{combinedY}],
                mode: 'lines',
                name: 'Combined Smooth GyroY (Media)',
                line: {{ color: '#1f77b4', width: 2 }}
            }};

            var layout = {{
                title: 'Rilevamento Curve su Combined Smooth GyroY',
                xaxis: {{ title: 'Timestamp' }},
                yaxis: {{ title: 'deg/s', range: [-250, 250] }},
                shapes: [{turnShapes}],
                hovermode: 'x unified',
                // Aggiungiamo una linea dello zero per riferimento
                shapes: [{turnShapes} {{
                    type: 'line', x0: {combinedData.First().Ts}, x1: {combinedData.Last().Ts},
                    y0: 0, y1: 0, line: {{ color: 'black', width: 1, dash: 'dot' }}
                }}]
            }};
            Plotly.newPlot('graph', [traceCombined], layout);
        </script>
    </body>
    </html>");

        File.WriteAllText(filePath, sb.ToString());
    }*/

    //ANALISI ROLL E G_FORCE
    public static void GenerateHtmlReport(List<SkiDataPoint> allPoints, List<SkiTurn> detectedTurns, string filePath = "SkiPhysicsAnalysis.html")
    {
        var leftData = allPoints.Where(p => p.Side == SensorSide.Left).ToList();
        var rightData = allPoints.Where(p => p.Side == SensorSide.Right).ToList();

        // Estrazione dati con cultura invariante (punto per i decimali)
        string leftRoll = string.Join(",", leftData.Select(p => p.SmoothRoll.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightRoll = string.Join(",", rightData.Select(p => p.SmoothRoll.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftAccX = string.Join(",", leftData.Select(p => p.SmoothAccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightAccX = string.Join(",", rightData.Select(p => p.SmoothAccX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftG = string.Join(",", leftData.Select(p => p.AdjustedG.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightG = string.Join(",", rightData.Select(p => p.AdjustedG.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string leftGyroY = string.Join(",", leftData.Select(p => p.SmoothGyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightGyroY = string.Join(",", rightData.Select(p => p.SmoothGyroY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string leftGyroX = string.Join(",", leftData.Select(p => p.SmoothGyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        string rightGyroX = string.Join(",", rightData.Select(p => p.SmoothGyroX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        string timestamps = string.Join(",", leftData.Select(p => p.Timestamp));

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
            var traceAccXL = {{ x: xData, y: [{leftAccX}], name: 'AccX L', yaxis: 'y2', line: {{color: 'green', width: 1.5, dash: 'dot'}} }};
            var traceAccXR = {{ x: xData, y: [{rightAccX}], name: 'AccX R', yaxis: 'y2', line: {{color: 'orange', width: 1.5, dash: 'dot'}} }};

            // 3. GYRO Y (Asse Y3 - Destra esterna)
            // Usiamo una linea continua sottile per distinguerla dalla G-Force
            var traceGyroYL = {{ x: xData, y: [{leftGyroY}], name: 'GyroY L (deg/s)', yaxis: 'y3', line: {{color: 'rgba(0, 128, 0, 0.6)', width: 1}} }};
            var traceGyroYR = {{ x: xData, y: [{rightGyroY}], name: 'GyroY R (deg/s)', yaxis: 'y3', line: {{color: 'rgba(139, 69, 19, 0.6)', width: 1}} }};
            var traceGyroXL = {{ x: xData, y: [{leftGyroX}], name: 'GyroZ L (deg/s)', yaxis: 'y3', line: {{color: 'red', width: 1}} }};
            var traceGyroXR = {{ x: xData, y: [{rightGyroX}], name: 'GyroZ R (deg/s)', yaxis: 'y3', line: {{color: 'blue', width: 1}} }};

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

            Plotly.newPlot('graph', [/*traceRollL, traceRollR,*/traceAccXL,traceAccXR, traceGL, traceGR, /*traceGyroYL, traceGyroYR,traceGyroXL, traceGyroXR*/], layout);
        </script>
    </body>
    </html>";

        File.WriteAllText(filePath, html);
    }
}