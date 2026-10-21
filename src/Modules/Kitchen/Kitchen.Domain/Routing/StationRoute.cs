namespace Kitchen.Domain;

/// <summary>« Ce produit est préparé à ce poste. »</summary>
public sealed record StationRoute(ProductId ProductId, StationId StationId);

/// <summary>« Ce produit est servi à ce service du repas » (entrée, plat, dessert).</summary>
public sealed record ProductCourse(ProductId ProductId, KitchenCourse Course);
