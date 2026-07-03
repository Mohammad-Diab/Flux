namespace FluxCore.Decoding;

/// <summary>
/// Projective mapping between two planes, computed from four point correspondences
/// (direct linear transform). Maps canonical tile-space coordinates to captured-image
/// pixel coordinates so tiles can be sampled from scaled, offset, or skewed captures.
/// </summary>
public sealed class Homography
{
    private readonly double[] _h;

    private Homography(double[] coefficients) => _h = coefficients;

    /// <summary>
    /// Computes the homography mapping each source point to its corresponding destination point.
    /// </summary>
    /// <param name="source">Four source points, no three collinear.</param>
    /// <param name="destination">Four corresponding destination points.</param>
    /// <exception cref="ArgumentException">Thrown when the points are degenerate (collinear).</exception>
    public static Homography FromPoints(
        IReadOnlyList<(double X, double Y)> source,
        IReadOnlyList<(double X, double Y)> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.Count != 4 || destination.Count != 4)
            throw new ArgumentException("Exactly four point correspondences are required.");

        var matrix = new double[8][];
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = source[i];
            var (u, v) = destination[i];
            matrix[2 * i] = [x, y, 1, 0, 0, 0, -u * x, -u * y, u];
            matrix[2 * i + 1] = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
        }

        return new Homography(SolveLinearSystem(matrix));
    }

    /// <summary>Maps a point from source space to destination space.</summary>
    /// <param name="x">Source X coordinate.</param>
    /// <param name="y">Source Y coordinate.</param>
    public (double X, double Y) Map(double x, double y)
    {
        double w = _h[6] * x + _h[7] * y + 1;
        return ((_h[0] * x + _h[1] * y + _h[2]) / w,
                (_h[3] * x + _h[4] * y + _h[5]) / w);
    }

    private static double[] SolveLinearSystem(double[][] rows)
    {
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 8; r++)
            {
                if (Math.Abs(rows[r][col]) > Math.Abs(rows[pivot][col]))
                    pivot = r;
            }

            if (Math.Abs(rows[pivot][col]) < 1e-12)
                throw new ArgumentException("Point correspondences are degenerate (collinear points).");

            (rows[col], rows[pivot]) = (rows[pivot], rows[col]);

            for (int r = 0; r < 8; r++)
            {
                if (r == col)
                    continue;

                double factor = rows[r][col] / rows[col][col];
                for (int c = col; c <= 8; c++)
                {
                    rows[r][c] -= factor * rows[col][c];
                }
            }
        }

        var solution = new double[8];
        for (int i = 0; i < 8; i++)
        {
            solution[i] = rows[i][8] / rows[i][i];
        }

        return solution;
    }
}
