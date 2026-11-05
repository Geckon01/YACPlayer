namespace Yacplayer.Dsp;

internal sealed class BeatTracker
{
    private const int HistoryCapacity = 4096;
    private const int MaximumAgents = 8;
    private const float MinimumOnsetPeak = 0.65f;
    private const double MinimumBpm = 55;
    private const double MaximumBpm = 200;

    private readonly float[] _history = new float[HistoryCapacity];
    private readonly long[] _historySamples = new long[HistoryCapacity];
    private readonly double[] _correlations = new double[2048];
    private readonly List<BeatAgent> _agents = new(MaximumAgents + 4);
    private int _historyWrite;
    private int _historyCount;
    private int _framesSinceEstimate;
    private int _sampleRate;
    private int _hopSize;
    private int _beatHoldFrames;
    private long _currentSample;
    private long _lastOnsetSample = long.MinValue;
    private double _lastEmittedBeatSample = double.NegativeInfinity;
    private BeatAgent? _activeAgent;

    public float Bpm => _activeAgent is null
        ? 0
        : (float)(60 * _sampleRate / _activeAgent.PeriodSamples);

    public float Confidence
    {
        get
        {
            if (_activeAgent is null || _sampleRate <= 0)
            {
                return 0;
            }

            var confidence = _activeAgent.CombinedScore;
            if (_lastOnsetSample != long.MinValue)
            {
                var silenceSeconds = (_currentSample - _lastOnsetSample) / (double)_sampleRate;
                if (silenceSeconds > 2)
                {
                    confidence *= Math.Exp(-(silenceSeconds - 2) / 4);
                }
            }

            return (float)Math.Clamp(confidence, 0, 1);
        }
    }

    public bool BeatDetected => _beatHoldFrames > 0 && Confidence >= 0.15f;

    public void AddFrame(float onset, long frameCenterSample, int sampleRate, int hopSize)
    {
        _sampleRate = sampleRate;
        _hopSize = hopSize;
        _currentSample = frameCenterSample;

        DetectOnsetPeak(onset);
        AddHistory(onset, frameCenterSample);

        _framesSinceEstimate++;
        var estimateInterval = Math.Max(1, (int)Math.Round(sampleRate / (double)hopSize / 2));
        if (_framesSinceEstimate >= estimateInterval)
        {
            _framesSinceEstimate = 0;
            EstimateTempo();
        }

        UpdatePredictedBeat(frameCenterSample);
    }

    public void Reset()
    {
        Array.Clear(_history);
        Array.Clear(_historySamples);
        Array.Clear(_correlations);
        _agents.Clear();
        _historyWrite = 0;
        _historyCount = 0;
        _framesSinceEstimate = 0;
        _sampleRate = 0;
        _hopSize = 0;
        _beatHoldFrames = 0;
        _currentSample = 0;
        _lastOnsetSample = long.MinValue;
        _lastEmittedBeatSample = double.NegativeInfinity;
        _activeAgent = null;
    }

    private void DetectOnsetPeak(float current)
    {
        if (_historyCount < 2)
        {
            return;
        }

        var previousPrevious = GetHistory(_historyCount - 2);
        var previous = GetHistory(_historyCount - 1);
        if (previous >= MinimumOnsetPeak && previous > previousPrevious && previous >= current)
        {
            ObserveOnset(GetHistorySample(_historyCount - 1), previous);
        }
    }

    private void ObserveOnset(long sample, float strength)
    {
        _lastOnsetSample = sample;
        var normalizedStrength = Math.Clamp(strength / 3f, 0.25f, 1f);
        foreach (var agent in _agents)
        {
            var predicted = NearestBeat(agent, sample);
            var error = sample - predicted;
            var gate = Math.Max(_hopSize * 2, agent.PeriodSamples * 0.2);
            if (Math.Abs(error) <= gate)
            {
                var match = 1 - Math.Abs(error) / gate;
                var weightedMatch = match * (0.5 + 0.5 * normalizedStrength);
                agent.TrackingScore = 0.86 * agent.TrackingScore + 0.14 * weightedMatch;

                var phaseGain = 0.18 * normalizedStrength;
                var tempoGain = 0.02 * normalizedStrength;
                agent.AnchorSample = predicted + phaseGain * error;
                agent.PeriodSamples = Math.Clamp(
                    agent.PeriodSamples + tempoGain * error,
                    60 * _sampleRate / MaximumBpm,
                    60 * _sampleRate / MinimumBpm);
            }
            else
            {
                agent.TrackingScore *= 0.97;
            }
        }
    }

    private void EstimateTempo()
    {
        var frameRate = _sampleRate / (double)_hopSize;
        var historyCount = Math.Min(_historyCount, (int)Math.Ceiling(frameRate * 12));
        if (historyCount < frameRate * 4)
        {
            return;
        }

        var start = _historyCount - historyCount;
        var minimumLag = Math.Max(2, (int)Math.Floor(frameRate * 60 / MaximumBpm));
        var maximumLag = Math.Min(
            Math.Min(historyCount / 2, _correlations.Length - 2),
            (int)Math.Ceiling(frameRate * 60 / MinimumBpm));

        Array.Clear(_correlations, minimumLag - 1, maximumLag - minimumLag + 3);
        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            double correlation = 0;
            double leftEnergy = 0;
            double rightEnergy = 0;
            for (var index = lag; index < historyCount; index++)
            {
                var left = GetHistory(start + index);
                var right = GetHistory(start + index - lag);
                correlation += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            var denominator = Math.Sqrt(leftEnergy * rightEnergy);
            _correlations[lag] = denominator <= 1e-9 ? 0 : correlation / denominator;
        }

        var lagCandidates = new List<(int Lag, double Score)>();
        for (var lag = minimumLag + 1; lag < maximumLag; lag++)
        {
            var score = _correlations[lag];
            if (score > 0.05 && score >= _correlations[lag - 1] && score >= _correlations[lag + 1])
            {
                lagCandidates.Add((lag, score));
            }
        }

        lagCandidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var candidates = new List<TempoCandidate>(MaximumAgents);
        foreach (var lagCandidate in lagCandidates.Take(12))
        {
            var refinedLag = RefineLag(lagCandidate.Lag);
            var bpm = 60 * frameRate / refinedLag;
            if (candidates.Any(candidate => Math.Abs(candidate.Bpm - bpm) / bpm < 0.035))
            {
                continue;
            }

            var phase = EvaluatePhase(start, historyCount, refinedLag);
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120) / 0.75, 2));
            var score = (0.45 * lagCandidate.Score + 0.55 * phase.Score) * (0.6 + 0.4 * prior);
            candidates.Add(new TempoCandidate(
                bpm,
                refinedLag * _hopSize,
                phase.AnchorSample,
                Math.Clamp(score, 0, 1)));
        }

        RefreshAgents(candidates);
    }

    private double RefineLag(int lag)
    {
        var left = _correlations[lag - 1];
        var center = _correlations[lag];
        var right = _correlations[lag + 1];
        var denominator = left - 2 * center + right;
        if (Math.Abs(denominator) < 1e-9)
        {
            return lag;
        }

        return lag + Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
    }

    private PhaseEstimate EvaluatePhase(int start, int count, double periodFrames)
    {
        Span<int> seedIndices = stackalloc int[16];
        Span<float> seedStrengths = stackalloc float[16];
        var seedCount = 0;
        double totalPeakStrength = 0;
        float maximumPeak = 0;

        for (var index = 1; index < count - 1; index++)
        {
            var value = GetHistory(start + index);
            if (value < MinimumOnsetPeak ||
                value < GetHistory(start + index - 1) ||
                value < GetHistory(start + index + 1))
            {
                continue;
            }

            totalPeakStrength += value;
            maximumPeak = Math.Max(maximumPeak, value);
            InsertStrongestSeed(seedIndices, seedStrengths, ref seedCount, index, value);
        }

        if (seedCount == 0 || maximumPeak <= 0)
        {
            return new(0, GetHistorySample(start + count - 1));
        }

        var bestScore = 0d;
        var bestSeed = seedIndices[0];
        for (var seed = 0; seed < seedCount; seed++)
        {
            var origin = (double)seedIndices[seed];
            while (origin - periodFrames >= 0)
            {
                origin -= periodFrames;
            }

            double matchedStrength = 0;
            var expectedBeats = 0;
            for (var position = (double)origin; position < count; position += periodFrames)
            {
                var center = (int)Math.Round(position);
                float peak = 0;
                for (var offset = -2; offset <= 2; offset++)
                {
                    var index = center + offset;
                    if (index >= 0 && index < count)
                    {
                        peak = Math.Max(peak, GetHistory(start + index));
                    }
                }

                matchedStrength += peak;
                expectedBeats++;
            }

            var precision = expectedBeats == 0
                ? 0
                : matchedStrength / (expectedBeats * maximumPeak);
            var coverage = totalPeakStrength <= 1e-9
                ? 0
                : Math.Min(1, matchedStrength / totalPeakStrength);
            var score = 0.55 * precision + 0.45 * coverage;
            if (score > bestScore)
            {
                bestScore = score;
                bestSeed = seedIndices[seed];
            }
        }

        return new(Math.Clamp(bestScore, 0, 1), GetHistorySample(start + bestSeed));
    }

    private static void InsertStrongestSeed(
        Span<int> indices,
        Span<float> strengths,
        ref int count,
        int index,
        float strength)
    {
        var insertion = count;
        while (insertion > 0 && strengths[insertion - 1] < strength)
        {
            insertion--;
        }

        if (insertion >= strengths.Length)
        {
            return;
        }

        var newCount = Math.Min(count + 1, strengths.Length);
        for (var move = newCount - 1; move > insertion; move--)
        {
            strengths[move] = strengths[move - 1];
            indices[move] = indices[move - 1];
        }

        strengths[insertion] = strength;
        indices[insertion] = index;
        count = newCount;
    }

    private void RefreshAgents(List<TempoCandidate> candidates)
    {
        foreach (var agent in _agents)
        {
            agent.EvidenceScore *= 0.82;
        }

        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score).Take(MaximumAgents))
        {
            BeatAgent? match = null;
            var bestDistance = double.MaxValue;
            foreach (var agent in _agents)
            {
                var tempoDistance = Math.Abs(agent.PeriodSamples - candidate.PeriodSamples) /
                                    candidate.PeriodSamples;
                var phaseDistance = Math.Abs(candidate.AnchorSample - NearestBeat(agent, candidate.AnchorSample)) /
                                    candidate.PeriodSamples;
                var distance = tempoDistance + phaseDistance;
                if (tempoDistance < 0.04 && phaseDistance < 0.25 && distance < bestDistance)
                {
                    match = agent;
                    bestDistance = distance;
                }
            }

            if (match is null)
            {
                _agents.Add(new BeatAgent(
                    candidate.PeriodSamples,
                    candidate.AnchorSample,
                    candidate.Score,
                    candidate.Score));
                continue;
            }

            var predicted = NearestBeat(match, candidate.AnchorSample);
            var phaseError = candidate.AnchorSample - predicted;
            match.AnchorSample = predicted + 0.2 * phaseError;
            match.PeriodSamples = 0.82 * match.PeriodSamples + 0.18 * candidate.PeriodSamples;
            match.EvidenceScore = Math.Max(
                match.EvidenceScore,
                0.65 * match.EvidenceScore + 0.35 * candidate.Score);
        }

        _agents.RemoveAll(agent => agent.EvidenceScore < 0.04 && agent.TrackingScore < 0.08);
        _agents.Sort((left, right) => right.CombinedScore.CompareTo(left.CombinedScore));
        if (_agents.Count > MaximumAgents)
        {
            _agents.RemoveRange(MaximumAgents, _agents.Count - MaximumAgents);
        }

        if (_activeAgent is not null && !_agents.Contains(_activeAgent))
        {
            _activeAgent = null;
        }

        if (_agents.Count == 0)
        {
            return;
        }

        var challenger = _agents[0];
        if (_activeAgent is null ||
            _activeAgent.CombinedScore < 0.12 ||
            (challenger != _activeAgent &&
             challenger.CombinedScore > _activeAgent.CombinedScore + 0.08 &&
             challenger.CombinedScore > _activeAgent.CombinedScore * 1.12))
        {
            _activeAgent = challenger;
            _lastEmittedBeatSample = double.NegativeInfinity;
        }
    }

    private void UpdatePredictedBeat(long sample)
    {
        if (_beatHoldFrames > 0)
        {
            _beatHoldFrames--;
        }

        if (_activeAgent is null || Confidence < 0.15f)
        {
            return;
        }

        var beatSample = NearestBeat(_activeAgent, sample);
        if (Math.Abs(sample - beatSample) <= _hopSize * 0.55 &&
            beatSample > _lastEmittedBeatSample + _activeAgent.PeriodSamples * 0.5)
        {
            _lastEmittedBeatSample = beatSample;
            _beatHoldFrames = Math.Max(1, (int)Math.Ceiling(0.09 * _sampleRate / _hopSize));
        }
    }

    private void AddHistory(float value, long sample)
    {
        _history[_historyWrite] = value;
        _historySamples[_historyWrite] = sample;
        _historyWrite = (_historyWrite + 1) % HistoryCapacity;
        _historyCount = Math.Min(HistoryCapacity, _historyCount + 1);
    }

    private float GetHistory(int chronologicalIndex)
    {
        var physical = (_historyWrite - _historyCount + chronologicalIndex + HistoryCapacity) %
                       HistoryCapacity;
        return _history[physical];
    }

    private long GetHistorySample(int chronologicalIndex)
    {
        var physical = (_historyWrite - _historyCount + chronologicalIndex + HistoryCapacity) %
                       HistoryCapacity;
        return _historySamples[physical];
    }

    private static double NearestBeat(BeatAgent agent, double sample)
    {
        var beat = Math.Round((sample - agent.AnchorSample) / agent.PeriodSamples);
        return agent.AnchorSample + beat * agent.PeriodSamples;
    }

    private sealed class BeatAgent(
        double periodSamples,
        double anchorSample,
        double evidenceScore,
        double trackingScore)
    {
        public double PeriodSamples { get; set; } = periodSamples;
        public double AnchorSample { get; set; } = anchorSample;
        public double EvidenceScore { get; set; } = evidenceScore;
        public double TrackingScore { get; set; } = trackingScore;
        public double CombinedScore => Math.Clamp(0.68 * EvidenceScore + 0.32 * TrackingScore, 0, 1);
    }

    private readonly record struct TempoCandidate(
        double Bpm,
        double PeriodSamples,
        long AnchorSample,
        double Score);

    private readonly record struct PhaseEstimate(double Score, long AnchorSample);
}
