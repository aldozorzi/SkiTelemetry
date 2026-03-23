using SkiTelemetry_Core;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

public class TurnDetector
{
    // Evento per notificare la fine di una curva
    public event EventHandler<SkiTurn>? OnTurnCompleted;

    // Metriche che vogliamo calcolare (settate da fuori)
    public HashSet<MonitorMode> RequestedMetrics { get; } = new();

    // Stato del sistema
    private enum State { Searching, Recording }
    private State _currentState = State.Searching;

    // Buffer per il "passato" (Pre-Trigger)
    private Queue<SkiFrame> _preBuffer = new Queue<SkiFrame>();
    private const int PreBufferSize = 15; // ~375ms a 40Hz: copre ampiamente l'inizio della spigolata

    // La "scatola nera" della curva: qui finisce tutto il dataset
    private List<SkiFrame> _currentTurnSamples = new List<SkiFrame>();

    // Soglie di comando
    private const double TriggerConfirmation = 45.0; // "Soglia di certezza" (Trigger)
    private const double ExitThreshold = 10.0;      // "Soglia di rilascio" (Fine curva)

    // Filtri di Qualità (Post-Evento)
    private const double MinPeakRequired = 80.0;    // Se non arrivi a 80, non è una curva vera
    private const int MinSamplesRequired = 12;     // Almeno 300ms di durata

    public void AddPoints(SkiDataPoint left, SkiDataPoint right)
    {
        ProcessFrame(left, right);
    }

    public void ProcessFrame(SkiDataPoint left, SkiDataPoint right)
    {
        var frame = new SkiFrame { Left = left, Right = right };
        double absMeanZ = Math.Abs(frame.CombinedGyroZ ?? 0);

        switch (_currentState)
        {
            case State.Searching:
                _preBuffer.Enqueue(frame);
                if (_preBuffer.Count > PreBufferSize) _preBuffer.Dequeue();

                if (absMeanZ >= TriggerConfirmation)
                {
                    StartRecording();
                }
                break;

            case State.Recording:
                _currentTurnSamples.Add(frame);

                if (absMeanZ < ExitThreshold)
                {
                    EndRecording();
                }
                break;
        }
    }

    private void StartRecording()
    {
        _currentState = State.Recording;
        _currentTurnSamples.Clear();

        // Travasiamo i frame completi (Acc, Gyro, Roll, Timestamps...)
        while (_preBuffer.Count > 0)
        {
            _currentTurnSamples.Add(_preBuffer.Dequeue());
        }
    }

    private void EndRecording()
    {
        double maxAbs = _currentTurnSamples.Select(f => Math.Abs(f.CombinedGyroZ ?? 0)).Max();

        if (_currentTurnSamples.Count >= MinSamplesRequired && maxAbs >= MinPeakRequired)
        {
            // ORA HAI TUTTO: puoi accedere a _currentTurnSamples[i].Left.AccX, ecc.
            AnalyzeTurn(_currentTurnSamples);
        }

        _currentTurnSamples.Clear();
        _preBuffer.Clear();
        _currentState = State.Searching;
    }

    private void AnalyzeTurn(List<SkiFrame> turnData)
    {
        // ESEMPIO DI COSA PUOI FARE ORA:
        var t0Frame = turnData.FirstOrDefault(f => Math.Abs(f.CombinedGyroZ ?? 0) > 5.0);
        uint startTime = t0Frame?.Left?.Timestamp ?? turnData[0].Left.Timestamp;
        uint endTime = turnData.Last().Left.Timestamp;


        var turn = new SkiTurn();
        turn.EdgeAngle = SkiTurnAnalyzer.CalculateMaxEdgeAngle(turnData);
        turn.EdgeSimilarity = SkiTurnAnalyzer.CalculateSimilarity(turnData);
        turn.EarlyEdging = 0;
        turn.GForce = SkiTurnAnalyzer.CalculateMaxGForce(turnData); ;
        turn.Radius = SkiTurnAnalyzer.CalculateEstimatedRadius(turnData);
        turn.Shape = 0;
        turn.ProgressiveEdgeBuild = 0;
        turn.CarvingIndex = SkiTurnAnalyzer.CalculateCarvingIndex(turnData);
        turn.Foot = SkiTurnAnalyzer.DetermineOuterFoot(turnData);
        turn.StartTimestamp = startTime;
        turn.EndTimestamp = endTime;
        OnTurnCompleted?.Invoke(this, turn);

        Console.WriteLine($"Curva analizzata: {endTime - startTime}ms.");
    }
}