using System;
using System.Collections.Generic;
using System.Linq;
public static class SplitFaceColor {
    public static bool Matches(IEnumerable<double> actual, IEnumerable<double> expected) {
        var a = actual.ToArray();
        var b = expected.ToArray();
        if (a.Length != 3 || b.Length != 3) return false;
        for (int i = 0; i < 3; i++) {
            if (a[i] == b[i]) continue;
            if (!(a[i] >= 0 && a[i] <= 1 && b[i] >= 0 && b[i] <= 1 &&
                  Math.Abs(a[i] - b[i]) <= 1.0 / 255 + 1e-10)) return false;
        }
        return true;
    }
}
