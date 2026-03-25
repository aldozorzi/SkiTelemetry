using SkiTelemetry_Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class SkiGraphGenerator
{
    // DETECTING CURVE
    public static void GenerateHtmlReport(List<SkiDataPoint> allPoints, List<SkiTurn> detectedTurns, string filePath = "SkiDebugGraph.html")
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
    }
    /* 
    //ANALISI ROLL E G_FORCE
    public static void GenerateHtmlReport(List<SkiDataPoint> allPoints, List<SkiTurn> detectedTurns, string filePath = "SkiPhysicsAnalysis.html")
    {
        var leftData = allPoints.Where(p => p.Side == SensorSide.Left).ToList();
        var rightData = allPoints.Where(p => p.Side == SensorSide.Right).ToList();

        // 1. Dati Inclinazione (Roll)
        string leftRoll = string.Join(",", leftData.Select(p => p.SmoothRoll.ToString().Replace(",", ".")));
        string rightRoll = string.Join(",", rightData.Select(p => p.SmoothRoll.ToString().Replace(",", ".")));

        // 2. Dati G-Force (Magnitudo calcolata dai dati Smooth)
        string leftG = string.Join(",", leftData.Select(p => {
            double g = Math.Sqrt(Math.Pow(p.SmoothAccX, 2) + Math.Pow(p.SmoothAccY, 2) + Math.Pow(p.SmoothAccZ, 2));
            return g.ToString("F2").Replace(",", ".");
        }));
        string rightG = string.Join(",", rightData.Select(p => {
            double g = Math.Sqrt(Math.Pow(p.SmoothAccX, 2) + Math.Pow(p.SmoothAccY, 2) + Math.Pow(p.SmoothAccZ, 2));
            return g.ToString("F2").Replace(",", ".");
        }));

        string timestamps = string.Join(",", leftData.Select(p => p.Timestamp));

        // Zone per le curve
        var turnShapes = new StringBuilder();
        foreach (var turn in detectedTurns)
        {
            turnShapes.AppendLine($"{{ type: 'rect', xref: 'x', yref: 'paper', x0: {turn.StartTimestamp}, x1: {turn.EndTimestamp}, y0: 0, y1: 1, fillcolor: 'rgba(255, 0, 0, 0.1)', line: {{ width: 0 }} }},");
        }

        var html = $@"
    <html>
    <head><script src='https://cdn.plot.ly/plotly-latest.min.js'></script></head>
    <body>
        <div id='graph' style='width:100%; height:95vh;'></div>
        <script>
            var xData = [{timestamps}];
            
            // Tracce Inclinazione (Asse Y1)
            var traceRollL = {{ x: xData, y: [{leftRoll}], name: 'Roll Left (deg)', line: {{color: 'rgba(255, 0, 0, 0.5)', width: 2}} }};
            var traceRollR = {{ x: xData, y: [{rightRoll}], name: 'Roll Right (deg)', line: {{color: 'rgba(31, 119, 180, 0.5)', width: 2}} }};

            // Tracce G-Force (Asse Y2 - tratteggiate)
            var traceGL = {{ x: xData, y: [{leftG}], name: 'G-Force Left', yaxis: 'y2', line: {{color: 'red', width: 1, dash: 'dot'}} }};
            var traceGR = {{ x: xData, y: [{rightG}], name: 'G-Force Right', yaxis: 'y2', line: {{color: '#1f77b4', width: 1, dash: 'dot'}} }};

            var layout = {{
                title: 'Analisi Correlazione: Inclinazione vs Pressione',
                xaxis: {{ title: 'Timestamp' }},
                yaxis: {{ title: 'Inclinazione (deg)', range: [-60, 60] }},
                yaxis2: {{ 
                    title: 'G-Force', 
                    overlaying: 'y', 
                    side: 'right', 
                    range: [0, 4],
                    showgrid: false 
                }},
                shapes: [{turnShapes}],
                hovermode: 'x'
            }};

            Plotly.newPlot('graph', [traceRollL, traceRollR, traceGL, traceGR], layout);
        </script>
    </body>
    </html>";

        File.WriteAllText(filePath, html);
    }*/
}