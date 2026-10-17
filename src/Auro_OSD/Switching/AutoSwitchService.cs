using System.Text;
using Auro_OSD.Capture;
using Auro_OSD.Config;
using Auro_OSD.Logging;

namespace Auro_OSD.Switching;

public sealed class AutoSwitchService : IDisposable
{
    private sealed record EntryEval(AutoSwitchEntry Entry, uint Value, bool Fresh, double Similarity, bool Active);

    private readonly AppConfig _config;
    private readonly CaptureSignalMonitor _capture = new();
    private readonly Func<uint, bool, bool> _performSwitch;
    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<string, List<byte[]>> _referenceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _activeState = new(StringComparer.OrdinalIgnoreCase);
    private int _running;

    private const int PollIntervalMs = 250;
    private const int StabilityMs = 500;

    public volatile bool Suspended;
    public DateTime PausedUntil = DateTime.MinValue;
    public uint? CurrentInput;

    public AutoSwitchService(AppConfig config, Func<uint, bool, bool> performSwitch)
    {
        _config = config;
        _performSwitch = performSwitch;
        _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        RefreshDevices();
        _timer.Change(1500, PollIntervalMs);
    }

    public void Refresh()
    {
        lock (_referenceCache) _referenceCache.Clear();
        RefreshDevices();
        _timer.Change(PollIntervalMs, PollIntervalMs);
    }

    private bool IsEligible(AutoSwitchEntry entry) =>
        entry.Enabled &&
        !string.IsNullOrWhiteSpace(entry.CaptureDevice) &&
        !string.IsNullOrWhiteSpace(entry.NoSignalSnapshot);

    public bool HasEnabledEntry => _config.AutoSwitch.Order.Any(IsEligible);

    private void RefreshDevices()
    {
        var ids = _config.AutoSwitch.Order
            .Where(IsEligible)
            .Select(e => e.CaptureDevice!)
            .ToList();

        _capture.SetDevices(ids);
        AppLogger.Line($"devices [{string.Join(", ", ids)}]");
    }

    public List<CapturedFrame>? CaptureFrames(string deviceId, string previewDir)
    {
        var wasSuspended = Suspended;
        Suspended = true;

        try
        {
            var ids = _config.AutoSwitch.Order.Where(IsEligible).Select(e => e.CaptureDevice!).ToList();
            if (!ids.Contains(deviceId, StringComparer.OrdinalIgnoreCase)) ids.Add(deviceId);
            _capture.SetDevices(ids);

            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && _capture.GetAgeMs(deviceId) < 0)
                Thread.Sleep(150);

            return _capture.CaptureFrames(deviceId, previewDir);
        }
        finally
        {
            Suspended = wasSuspended;
        }
    }

    private void Tick(object? state)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;

        try
        {
            if (Suspended) return;
            if (DateTime.UtcNow < PausedUntil) return;
            Evaluate();
        }
        catch
        {
            // never let the timer die
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void Evaluate()
    {
        var order = _config.AutoSwitch.Order
            .Where(IsEligible)
            .Select(e => new { Entry = e, Value = ValueOf(e.Input) })
            .Where(r => r.Value.HasValue)
            .ToList();

        if (order.Count == 0)
        {
            ResetCandidate();
            return;
        }

        var current = CurrentInput;
        var dp = _config.InputMap.Dp1;

        var evals = order
            .Select(r =>
            {
                var fresh = IsFresh(r.Entry);
                var similarity = fresh ? MaxSimilarity(r.Entry) : -1.0;
                var active = fresh && similarity >= 0 && r.Entry.SimilarityThreshold >= 0.9 &&
                             similarity < r.Entry.SimilarityThreshold;
                return new EntryEval(r.Entry, r.Value!.Value, fresh, similarity, active);
            })
            .ToList();

        LogPoll(current, evals);

        uint? targetValue = null;
        var targetIsDp = false;

        if (dp.HasValue && current == dp.Value)
        {
            var active = evals.FirstOrDefault(e => e.Active);
            if (active != null)
            {
                targetValue = active.Value;
                targetIsDp = false;
            }
        }
        else if (current != null)
        {
            var currentRow = evals.FirstOrDefault(e => e.Value == current);
            if (currentRow == null)
            {
                ResetCandidate();
                return;
            }

            if (!currentRow.Fresh) return;

            if (!currentRow.Active)
            {
                var next = evals.FirstOrDefault(e => e.Active);
                if (next != null)
                {
                    targetValue = next.Value;
                    targetIsDp = false;
                }
                else if (dp.HasValue)
                {
                    targetValue = dp.Value;
                    targetIsDp = true;
                }
            }
        }

        if (targetValue == null)
        {
            ResetCandidate();
            return;
        }

        if (_candidateValue == targetValue && _candidateIsDp == targetIsDp)
        {
            if ((DateTime.UtcNow - _candidateSince).TotalMilliseconds >= StabilityMs)
            {
                AppLogger.Line($"switch-req value={targetValue} isDp={(targetIsDp ? 1 : 0)}");
                if (targetIsDp) SwitchDp();
                else SwitchHdmi(targetValue.Value);
                ResetCandidate();
            }
        }
        else
        {
            _candidateValue = targetValue;
            _candidateIsDp = targetIsDp;
            _candidateSince = DateTime.UtcNow;
        }
    }

    private void LogPoll(uint? current, List<EntryEval> evals)
    {
        if (!AppLogger.Enabled) return;

        foreach (var e in evals)
        {
            var previous = _activeState.TryGetValue(e.Entry.Input, out var value) && value;
            if (previous != e.Active)
            {
                _activeState[e.Entry.Input] = e.Active;
                AppLogger.Line($"active {e.Entry.Input} {(previous ? 1 : 0)}->{(e.Active ? 1 : 0)} sim={(e.Similarity >= 0 ? e.Similarity.ToString("F3") : "n/a")}");
            }
        }

        var sb = new StringBuilder();
        sb.Append("poll current=").Append(current?.ToString() ?? "?");
        foreach (var e in evals)
        {
            sb.Append(" | ").Append(e.Entry.Input)
              .Append(" sim=").Append(e.Similarity >= 0 ? e.Similarity.ToString("F3") : "n/a")
              .Append(" thr=").Append(e.Entry.SimilarityThreshold.ToString("F2"))
              .Append(" fresh=").Append(e.Fresh ? 1 : 0)
              .Append(" act=").Append(e.Active ? 1 : 0);
        }

        if (_candidateValue != null)
            sb.Append(" cand=").Append(_candidateValue)
              .Append(" stable=").Append((int)(DateTime.UtcNow - _candidateSince).TotalMilliseconds).Append("ms");

        AppLogger.Line(sb.ToString());
    }

    private uint? _candidateValue;
    private bool _candidateIsDp;
    private DateTime _candidateSince = DateTime.MinValue;

    private void ResetCandidate()
    {
        _candidateValue = null;
        _candidateSince = DateTime.MinValue;
    }

    private bool IsFresh(AutoSwitchEntry entry)
    {
        if (entry.CaptureDevice == null) return false;
        var age = _capture.GetAgeMs(entry.CaptureDevice);
        var hold = Math.Max(_config.AutoSwitch.ActiveHoldMs, 2500);
        return age >= 0 && age <= hold;
    }

    private double MaxSimilarity(AutoSwitchEntry entry)
    {
        if (entry.CaptureDevice == null) return -1;

        var current = _capture.GetSignature(entry.CaptureDevice);
        if (current == null) return -1;

        var references = GetReferences(entry);
        if (references.Count == 0) return -1;

        var max = 0.0;
        foreach (var reference in references)
        {
            var similarity = CaptureSignalMonitor.Similarity(current, reference);
            if (similarity > max) max = similarity;
        }

        return max;
    }

    private List<byte[]> GetReferences(AutoSwitchEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.NoSignalSnapshot))
            return new List<byte[]>();

        lock (_referenceCache)
        {
            if (_referenceCache.TryGetValue(entry.NoSignalSnapshot, out var cached))
                return cached;

            var signatures = CaptureSignalMonitor.LoadSignatures(entry.NoSignalSnapshot);
            _referenceCache[entry.NoSignalSnapshot] = signatures;
            return signatures;
        }
    }

    private uint? ValueOf(string input) => input switch
    {
        "Hdmi1" => _config.InputMap.Hdmi1,
        "Hdmi2" => _config.InputMap.Hdmi2,
        _ => null
    };

    private void SwitchHdmi(uint value)
    {
        if (CurrentInput == value) return;

        if (_performSwitch(value, false))
            CurrentInput = value;
    }

    private void SwitchDp()
    {
        if (_config.InputMap.Dp1 is not uint dp) return;
        if (CurrentInput == dp) return;
        if (_performSwitch(dp, true))
            CurrentInput = dp;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _capture.Dispose();
    }
}
