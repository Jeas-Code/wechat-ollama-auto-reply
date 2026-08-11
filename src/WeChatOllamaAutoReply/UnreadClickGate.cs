namespace WeChatOllamaAutoReply;

public sealed class UnreadClickGate(
    int rowTolerance = 10,
    int requiredStableFrames = 3,
    int missingFramesToForget = 2)
{
    private readonly List<Track> _tracks = [];

    public void Initialize(IEnumerable<UnreadSession> baseline)
    {
        _tracks.Clear();
        foreach (var marker in baseline.OrderBy(marker => marker.RowY))
        {
            _tracks.Add(new Track(marker, locked: true));
        }
    }

    public IReadOnlyList<UnreadSession> Observe(IEnumerable<UnreadSession> observations)
    {
        var current = observations.OrderBy(marker => marker.RowY).ToArray();
        var trackMatches = new int?[_tracks.Count];
        var observationMatched = new bool[current.Length];

        MatchByStableContact(current, trackMatches, observationMatched);
        MatchByRow(current, trackMatches, observationMatched);

        var unmatchedTracks = Enumerable.Range(0, _tracks.Count)
            .Where(index => trackMatches[index] is null)
            .ToArray();
        var ambiguousLayoutChange = unmatchedTracks.Length > 0;

        for (var index = 0; index < _tracks.Count; index++)
        {
            if (trackMatches[index] is int observationIndex)
            {
                _tracks[index].Update(current[observationIndex]);
            }
            else
            {
                _tracks[index].MissingFrames++;
            }
        }

        for (var index = 0; index < current.Length; index++)
        {
            if (!observationMatched[index])
            {
                _tracks.Add(new Track(current[index], locked: ambiguousLayoutChange));
            }
        }

        _tracks.RemoveAll(track => track.MissingFrames >= missingFramesToForget);

        var ready = new List<UnreadSession>();
        foreach (var track in _tracks.Where(track => track.IsReady(requiredStableFrames)))
        {
            track.Locked = true;
            ready.Add(track.Latest);
        }

        return ready;
    }

    private void MatchByStableContact(
        IReadOnlyList<UnreadSession> current,
        IList<int?> trackMatches,
        IList<bool> observationMatched)
    {
        for (var trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
        {
            var key = _tracks[trackIndex].ContactKey;
            if (key.Length == 0)
            {
                continue;
            }

            var candidates = Enumerable.Range(0, current.Count)
                .Where(index => !observationMatched[index])
                .Where(index => VisualMessagePolicy.ContactKey(current[index].Contact) == key)
                .OrderBy(index => Math.Abs(current[index].RowY - _tracks[trackIndex].Latest.RowY))
                .ToArray();
            if (candidates.Length == 1)
            {
                trackMatches[trackIndex] = candidates[0];
                observationMatched[candidates[0]] = true;
            }
        }
    }

    private void MatchByRow(
        IReadOnlyList<UnreadSession> current,
        IList<int?> trackMatches,
        IList<bool> observationMatched)
    {
        for (var trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
        {
            if (trackMatches[trackIndex] is not null)
            {
                continue;
            }

            var candidate = Enumerable.Range(0, current.Count)
                .Where(index => !observationMatched[index])
                .Select(index => (Index: index, Distance: Math.Abs(current[index].RowY - _tracks[trackIndex].Latest.RowY)))
                .Where(item => item.Distance <= rowTolerance)
                .OrderBy(item => item.Distance)
                .FirstOrDefault((Index: -1, Distance: int.MaxValue));
            if (candidate.Index >= 0)
            {
                trackMatches[trackIndex] = candidate.Index;
                observationMatched[candidate.Index] = true;
            }
        }
    }

    private sealed class Track
    {
        public Track(UnreadSession marker, bool locked)
        {
            Latest = marker;
            Locked = locked;
            StableFrames = 1;
            ContactKey = VisualMessagePolicy.ContactKey(marker.Contact);
            PreviewKey = VisualMessagePolicy.Normalize(marker.Preview);
        }

        public UnreadSession Latest { get; private set; }
        public string ContactKey { get; private set; }
        public string PreviewKey { get; private set; }
        public int StableFrames { get; private set; }
        public int MissingFrames { get; set; }
        public bool Locked { get; set; }

        public void Update(UnreadSession marker)
        {
            var contactKey = VisualMessagePolicy.ContactKey(marker.Contact);
            var previewKey = VisualMessagePolicy.Normalize(marker.Preview);
            StableFrames = contactKey.Length > 0 && previewKey.Length > 0 &&
                           contactKey == ContactKey && previewKey == PreviewKey
                ? StableFrames + 1
                : 1;
            ContactKey = contactKey;
            PreviewKey = previewKey;
            Latest = marker;
            MissingFrames = 0;
        }

        public bool IsReady(int requiredStableFrames) =>
            !Locked && MissingFrames == 0 && StableFrames >= requiredStableFrames &&
            ContactKey.Length >= 2 && PreviewKey.Length > 0;
    }
}
