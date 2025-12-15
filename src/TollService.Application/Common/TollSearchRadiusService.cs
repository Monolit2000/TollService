using TollService.Application.Common.Interfaces;
using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Common;

/// <summary>
/// Вычисляет/назначает радиусы поиска вокруг toll-точек так, чтобы окружности не пересекались.
/// </summary>
public class TollSearchRadiusService : ITollSearchRadiusService
{
    // Небольшой зазор, чтобы окружности не "касались" из-за погрешностей расчёта
    private const double ClearanceMeters = 0.1;
    private const int DuplicateCoordRoundDigits = 7; // ~ 1см, чтобы схлопывать "одинаковые" точки

    public void ApplyNonOverlappingRadii(IList<TollDto> tolls, double defaultRadiusMeters = 500.0)
    {
        if (tolls == null || tolls.Count == 0)
            return;

        // Детерминизм: всегда один и тот же результат при одинаковом входе
        var ordered = tolls
            .OrderBy(t => t.Id)
            .ToList();

        // Инициализация: всем выставляем дефолт (если координаты невалидны — 0)
        foreach (var t in ordered)
        {
            if (IsValidLatLon(t.Latitude, t.Longitude))
                t.SerchRadiusInMeters = defaultRadiusMeters;
            else
                t.SerchRadiusInMeters = 0;
        }

        // Жадный проход по всем парам:
        // если r_i + r_j > distance(i,j) - clearance => уменьшаем радиусы так, чтобы равенство стало выполняться.
        for (int i = 0; i < ordered.Count; i++)
        {
            var a = ordered[i];
            if (a.SerchRadiusInMeters <= 0)
                continue;

            for (int j = i + 1; j < ordered.Count; j++)
            {
                var b = ordered[j];
                if (b.SerchRadiusInMeters <= 0)
                    continue;

                // Если координаты невалидны — пропускаем
                if (!IsValidLatLon(a.Latitude, a.Longitude) || !IsValidLatLon(b.Latitude, b.Longitude))
                    continue;

                var d = HaversineDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                var allowedSum = Math.Max(0.0, d - ClearanceMeters);

                var sum = a.SerchRadiusInMeters + b.SerchRadiusInMeters;
                if (sum <= allowedSum)
                    continue;

                ReducePairToAllowedSum(a, b, allowedSum);
            }
        }
    }

    public void  ApplyNonOverlappingRadii(IList<Toll> tolls, double defaultRadiusMeters = 500.0, bool isRecursivePass = false)
    {
        if (tolls == null || tolls.Count == 0)
            return;

        var ordered = tolls
            .OrderBy(t => t.Id)
            .ToList();

        foreach (var t in ordered)
        {
            var lat = t.Location?.Y ?? 0;
            var lon = t.Location?.X ?? 0;

            if (t.Location != null && IsValidLatLon(lat, lon))
                t.SerchRadiusInMeters = defaultRadiusMeters;
            else
                t.SerchRadiusInMeters = 0;
        }

        // Исключаем дубли по координатам: считаем радиусы только на уникальных точках,
        // затем присваиваем результат всем toll'ам в группе (иначе при одинаковом Location d=0 => радиусы схлопываются в 0).
        var groupsByLocation = ordered
            .Where(t => t.Location != null && IsValidLatLon(t.Location!.Y, t.Location!.X))
            .GroupBy(t => BuildLocationKey(t.Location!))
            .ToList();

        var unique = groupsByLocation
            .Select(g => g.First())
            .ToList();

        for (int i = 0; i < unique.Count; i++)
        {
            var a = unique[i];
            if (a.SerchRadiusInMeters <= 0 || a.Location == null)
                continue;

            var aLat = a.Location.Y;
            var aLon = a.Location.X;

            for (int j = i + 1; j < unique.Count; j++)
            {
                var b = unique[j];
                if (b.SerchRadiusInMeters <= 0 || b.Location == null)
                    continue;

                var bLat = b.Location.Y;
                var bLon = b.Location.X;

                if (!IsValidLatLon(aLat, aLon) || !IsValidLatLon(bLat, bLon))
                    continue;

                var d = HaversineDistanceMeters(aLat, aLon, bLat, bLon);
                var allowedSum = Math.Max(0.0, d - ClearanceMeters);

                var sum = a.SerchRadiusInMeters + b.SerchRadiusInMeters;
                if (sum <= allowedSum)
                    continue;

                ReducePairToAllowedSum(a, b, allowedSum);
            }
        }

        // Проставляем рассчитанный радиус всем дублям в группе
        foreach (var group in groupsByLocation)
        {
            var r = group.First().SerchRadiusInMeters;
            foreach (var t in group)
                t.SerchRadiusInMeters = r;
        }


        if (!isRecursivePass)
        {
            ApplyNonOverlappingRadii(tolls.Where(x => x.SerchRadiusInMeters < 2).ToList(), defaultRadiusMeters, true);
        }
    }

    private static string BuildLocationKey(NetTopologySuite.Geometries.Point p)
    {
        // Point.X = lon, Point.Y = lat
        var lat = Math.Round(p.Y, DuplicateCoordRoundDigits);
        var lon = Math.Round(p.X, DuplicateCoordRoundDigits);
        return $"{lat}|{lon}";
    }

    private static void ReducePairToAllowedSum(Toll a, Toll b, double allowedSum)
    {
        var sum = a.SerchRadiusInMeters + b.SerchRadiusInMeters;
        if (sum <= allowedSum)
            return;

        var excess = sum - allowedSum;
        var half = excess / 2.0;

        var reduceA = Math.Min(half, a.SerchRadiusInMeters);
        var reduceB = Math.Min(half, b.SerchRadiusInMeters);

        a.SerchRadiusInMeters -= reduceA;
        b.SerchRadiusInMeters -= reduceB;

        var remaining = excess - (reduceA + reduceB);
        if (remaining <= 0)
            return;

        if (a.SerchRadiusInMeters > 0)
        {
            var take = Math.Min(remaining, a.SerchRadiusInMeters);
            a.SerchRadiusInMeters -= take;
            remaining -= take;
        }

        if (remaining > 0 && b.SerchRadiusInMeters > 0)
        {
            var take = Math.Min(remaining, b.SerchRadiusInMeters);
            b.SerchRadiusInMeters -= take;
        }
    }

    private static void ReducePairToAllowedSum(TollDto a, TollDto b, double allowedSum)
    {
        var sum = a.SerchRadiusInMeters + b.SerchRadiusInMeters;
        if (sum <= allowedSum)
            return;

        var excess = sum - allowedSum;

        // Пытаемся уменьшать поровну, но не ниже 0.
        var half = excess / 2.0;

        var reduceA = Math.Min(half, a.SerchRadiusInMeters);
        var reduceB = Math.Min(half, b.SerchRadiusInMeters);

        a.SerchRadiusInMeters -= reduceA;
        b.SerchRadiusInMeters -= reduceB;

        var remaining = excess - (reduceA + reduceB);
        if (remaining <= 0)
            return;

        // Если один радиус упёрся в 0, добираем остаток со второго
        if (a.SerchRadiusInMeters > 0)
        {
            var take = Math.Min(remaining, a.SerchRadiusInMeters);
            a.SerchRadiusInMeters -= take;
            remaining -= take;
        }

        if (remaining > 0 && b.SerchRadiusInMeters > 0)
        {
            var take = Math.Min(remaining, b.SerchRadiusInMeters);
            b.SerchRadiusInMeters -= take;
        }
    }

    private static bool IsValidLatLon(double lat, double lon)
        => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    /// <summary>
    /// Хаверсинус: расстояние между двумя точками на сфере (в метрах).
    /// </summary>
    private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6_371_000;

        var phi1 = DegreesToRadians(lat1);
        var phi2 = DegreesToRadians(lat2);
        var dPhi = DegreesToRadians(lat2 - lat1);
        var dLambda = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}


