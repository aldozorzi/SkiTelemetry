using SkiTelemetry_Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class SkiGraphGenerator
{
    public static void GenerateHtmlReport(List<SkiDataPoint> allPoints, List<SkiTurn> detectedTurns, string filePath = "SkiDebugGraph.html")
    {
        // Dividiamo i dati mantenendo l'ordine cronologico originale
        var leftData = allPoints.Where(p => p.Side == SensorSide.Left).ToList();
        var rightData = allPoints.Where(p => p.Side == SensorSide.Right).ToList();

        // Trasformiamo in stringhe per il JavaScript
        string leftY = string.Join(",", leftData.Select(p => p.GyroZ.ToString().Replace(",", ".")));
        string leftX = string.Join(",", leftData.Select(p => p.Timestamp));

        string rightY = string.Join(",", rightData.Select(p => p.GyroZ.ToString().Replace(",", ".")));
        string rightX = string.Join(",", rightData.Select(p => p.Timestamp));

        // Rettangoli per le curve (Zone Rosse)
        var turnShapes = new StringBuilder();
        foreach (var turn in detectedTurns)
        {
            turnShapes.AppendLine($@"{{
                type: 'rect', xref: 'x', yref: 'paper',
                x0: {turn.StartTimestamp}, x1: {turn.EndTimestamp},
                y0: 0, y1: 1,
                fillcolor: 'rgba(255, 0, 0, 0.2)',
                line: {{ width: 0 }}
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
                var traceL = {{
                    x: [{leftX}],
                    y: [{leftY}],
                    mode: 'lines',
                    name: 'LGyroZ (Left)',
                    line: {{ color: 'red', width: 1 }}
                }};

                var traceR = {{
                    x: [{rightX}],
                    y: [{rightY}],
                    mode: 'lines',
                    name: 'GyroZ (Right)',
                    line: {{ color: '#1f77b4', width: 1 }}
                }};

                var layout = {{
                    title: 'Analisi Comparativa GyroZ',
                    xaxis: {{ title: 'Timestamp' }},
                    yaxis: {{ title: 'deg/s', range: [-250, 250] }},
                    shapes: [{turnShapes}],
                    hovermode: 'x unified'
                }};

                Plotly.newPlot('graph', [traceL, traceR], layout);
            </script>
        </body>
        </html>");

        File.WriteAllText(filePath, sb.ToString());
    }
}