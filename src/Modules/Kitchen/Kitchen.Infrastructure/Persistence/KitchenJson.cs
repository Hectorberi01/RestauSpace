using System.Text.Json;
using Kitchen.Domain;

namespace Kitchen.Infrastructure.Persistence;

/// <summary>Formes jsonb des listes (routes d'un routage, options d'une ligne de ticket).</summary>
internal static class KitchenJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteRoutes(List<StationRoute> routes) => JsonSerializer.Serialize(
        routes.Select(route => new RouteRow(route.ProductId.Value, route.StationId.Value)).ToList(), Options);

    // Relu via les fabriques des ids : un id vide stocké échoue bruyamment au lieu de se charger.
    public static List<StationRoute> ReadRoutes(string json) =>
        (JsonSerializer.Deserialize<List<RouteRow>>(json, Options) ?? new List<RouteRow>())
        .Select(row => new StationRoute(ProductId.From(row.ProductId), StationId.From(row.StationId)))
        .ToList();

    public static string WriteCourses(List<ProductCourse> courses) => JsonSerializer.Serialize(
        courses.Select(course => new CourseRow(course.ProductId.Value, (int)course.Course)).ToList(), Options);

    public static List<ProductCourse> ReadCourses(string json) =>
        (JsonSerializer.Deserialize<List<CourseRow>>(json, Options) ?? new List<CourseRow>())
        .Select(row => new ProductCourse(ProductId.From(row.ProductId), Enum.IsDefined((KitchenCourse)row.Course) ? (KitchenCourse)row.Course : throw new InvalidOperationException("Unknown course.")))
        .ToList();

    public static string WriteNames(List<string> names) => JsonSerializer.Serialize(names, Options);

    public static List<string> ReadNames(string json) => JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();

    internal sealed record RouteRow(Guid ProductId, Guid StationId);

    internal sealed record CourseRow(Guid ProductId, int Course);
}
