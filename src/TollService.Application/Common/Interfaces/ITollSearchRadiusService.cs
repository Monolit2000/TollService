using TollService.Contracts;
using TollService.Domain;

namespace TollService.Application.Common.Interfaces;

public interface ITollSearchRadiusService
{
    /// <summary>
    /// Проставляет всем толлам радиус (default 500м) и уменьшает радиусы так,
    /// чтобы окружности поиска не пересекались (в рамках переданного набора).
    /// </summary>
    void ApplyNonOverlappingRadii(IList<TollDto> tolls, double defaultRadiusMeters = 500.0);

    /// <summary>
    /// То же самое, но для доменной модели (для сохранения в БД).
    /// </summary>
    void ApplyNonOverlappingRadii(IList<Toll> tolls, double defaultRadiusMeters = 500.0, bool isRecursivePass = false);
}


