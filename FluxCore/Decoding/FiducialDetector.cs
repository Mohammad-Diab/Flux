namespace FluxCore.Decoding;

/// <summary>
/// A detected finder pattern center in captured-image pixel coordinates.
/// </summary>
/// <param name="X">Sub-pixel X coordinate.</param>
/// <param name="Y">Sub-pixel Y coordinate.</param>
/// <param name="ModuleSize">Estimated finder module (tile) size in pixels.</param>
public readonly record struct FinderPoint(double X, double Y, double ModuleSize);

/// <summary>
/// Locates the four corner finder patterns in a captured image by scanning rows for the
/// QR-style 1:1:3:1:1 dark/light run profile, cross-checking vertically, and clustering
/// candidate centers. Run-length matching is scale-free, so scaled and offset captures work.
/// </summary>
public static class FiducialDetector
{
    private const int MinClusterHits = 3;

    /// <summary>
    /// Attempts to locate the four finder centers.
    /// </summary>
    /// <param name="image">Captured image luma.</param>
    /// <param name="corners">On success: the four centers ordered top-left, top-right, bottom-left, bottom-right (in image orientation).</param>
    /// <returns>True when exactly four well-supported finder clusters are found.</returns>
    public static bool TryDetect(LumaImage image, out FinderPoint[] corners)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clusters = new List<Cluster>();
        for (int y = 0; y < image.Height; y++)
        {
            ScanRow(image, y, clusters);
        }

        var supported = clusters.Where(c => c.Count >= MinClusterHits).ToList();
        if (supported.Count < 4)
        {
            corners = [];
            return false;
        }

        corners = PickExtremalCorners(supported);
        return corners.Length == 4;
    }

    private static void ScanRow(LumaImage image, int y, List<Cluster> clusters)
    {
        var runs = new List<(bool Dark, int Start, int Length)>();
        bool currentDark = image.IsDark(0, y);
        int runStart = 0;

        for (int x = 1; x <= image.Width; x++)
        {
            bool dark = x < image.Width && image.IsDark(x, y);
            if (x < image.Width && dark == currentDark)
                continue;

            runs.Add((currentDark, runStart, x - runStart));
            currentDark = dark;
            runStart = x;
        }

        Span<int> lengths = stackalloc int[5];
        for (int i = 0; i + 4 < runs.Count; i++)
        {
            if (!runs[i].Dark)
                continue;

            for (int j = 0; j < 5; j++)
            {
                lengths[j] = runs[i + j].Length;
            }

            if (!MatchesFinderProfile(lengths, out double module))
                continue;

            double centerX = runs[i + 2].Start + runs[i + 2].Length / 2.0;
            if (CrossCheckVertical(image, (int)Math.Round(centerX), y, module, out double centerY))
            {
                AddToCluster(clusters, centerX, centerY, module);
            }
        }
    }

    private static bool MatchesFinderProfile(ReadOnlySpan<int> runs, out double module)
    {
        int total = runs[0] + runs[1] + runs[2] + runs[3] + runs[4];
        module = total / 7.0;

        if (total < 7)
            return false;

        double tolerance = module / 2.0 + 0.5;
        return Math.Abs(runs[0] - module) <= tolerance &&
               Math.Abs(runs[1] - module) <= tolerance &&
               Math.Abs(runs[2] - 3 * module) <= 3 * tolerance &&
               Math.Abs(runs[3] - module) <= tolerance &&
               Math.Abs(runs[4] - module) <= tolerance;
    }

    private static bool CrossCheckVertical(LumaImage image, int centerX, int startY, double module, out double centerY)
    {
        centerY = 0;
        if (centerX < 0 || centerX >= image.Width || !image.IsDark(centerX, startY))
            return false;

        int maxRun = (int)(module * 5) + 2;
        Span<int> runs = stackalloc int[5];

        int y = startY;
        while (y >= 0 && image.IsDark(centerX, y) && runs[2] <= maxRun) { runs[2]++; y--; }
        int centerAbove = runs[2];
        if (y >= 0)
        {
            while (y >= 0 && !image.IsDark(centerX, y) && runs[1] <= maxRun) { runs[1]++; y--; }
            while (y >= 0 && image.IsDark(centerX, y) && runs[0] <= maxRun) { runs[0]++; y--; }
        }

        y = startY + 1;
        while (y < image.Height && image.IsDark(centerX, y) && runs[2] <= maxRun) { runs[2]++; y++; }
        if (y < image.Height)
        {
            while (y < image.Height && !image.IsDark(centerX, y) && runs[3] <= maxRun) { runs[3]++; y++; }
            while (y < image.Height && image.IsDark(centerX, y) && runs[4] <= maxRun) { runs[4]++; y++; }
        }

        if (!MatchesFinderProfile(runs, out _))
            return false;

        centerY = startY - centerAbove + runs[2] / 2.0 + 0.5;
        return true;
    }

    private static void AddToCluster(List<Cluster> clusters, double x, double y, double module)
    {
        foreach (var cluster in clusters)
        {
            double dx = x - cluster.MeanX;
            double dy = y - cluster.MeanY;
            if (Math.Sqrt(dx * dx + dy * dy) <= 2 * cluster.MeanModule)
            {
                cluster.Add(x, y, module);
                return;
            }
        }

        var fresh = new Cluster();
        fresh.Add(x, y, module);
        clusters.Add(fresh);
    }

    private static FinderPoint[] PickExtremalCorners(List<Cluster> clusters)
    {
        var topLeft = clusters.MinBy(c => c.MeanX + c.MeanY)!;
        var bottomRight = clusters.MaxBy(c => c.MeanX + c.MeanY)!;
        var topRight = clusters.MaxBy(c => c.MeanX - c.MeanY)!;
        var bottomLeft = clusters.MinBy(c => c.MeanX - c.MeanY)!;

        var picked = new[] { topLeft, topRight, bottomLeft, bottomRight };
        if (picked.Distinct().Count() != 4)
            return [];

        return picked.Select(c => new FinderPoint(c.MeanX, c.MeanY, c.MeanModule)).ToArray();
    }

    private sealed class Cluster
    {
        private double _sumX;
        private double _sumY;
        private double _sumModule;

        public int Count { get; private set; }

        public double MeanX => _sumX / Count;

        public double MeanY => _sumY / Count;

        public double MeanModule => _sumModule / Count;

        public void Add(double x, double y, double module)
        {
            _sumX += x;
            _sumY += y;
            _sumModule += module;
            Count++;
        }
    }
}
