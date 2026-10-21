using Promotions.Application;
using Promotions.Domain;

namespace Promotions.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record CreatePromotionRequest(string? Name, PromotionRulesInput Rules);

/// <param name="Policy">Une fois la promotion utilisée : <c>NonDestructiveOnly</c> (défaut), <c>Frozen</c> ou <c>Unrestricted</c>.</param>
public sealed record ChangeRulesRequest(PromotionRulesInput Rules, RuleChangePolicy? Policy = null);

public sealed record CreateCouponRequest(string? Code, int MaxUses);

/// <summary>Soit une promotion sans coupon, soit le code présenté par le client.</summary>
public sealed record ApplyPromotionRequest(Guid? PromotionId = null, string? CouponCode = null);
