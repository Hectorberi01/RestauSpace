using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Reviews.Domain;

namespace Reviews.Infrastructure.Persistence;

internal static class ReviewsConverters
{
    public static readonly ValueConverter<TenantId, Guid> TenantKey = new(id => id.Value, value => TenantId.From(value));
    public static readonly ValueConverter<LocationId, Guid> LocationKey = new(id => id.Value, value => LocationId.From(value));
    public static readonly ValueConverter<CustomerId, Guid> CustomerKey = new(id => id.Value, value => CustomerId.From(value));
    public static readonly ValueConverter<OrderId, Guid> OrderKey = new(id => id.Value, value => OrderId.From(value));
    public static readonly ValueConverter<UserId, Guid> UserKey = new(id => id.Value, value => UserId.From(value));
    public static readonly ValueConverter<FeedbackId, Guid> FeedbackKey = new(id => id.Value, value => FeedbackId.From(value));
    public static readonly ValueConverter<SurveyId, Guid> SurveyKey = new(id => id.Value, value => SurveyId.From(value));
    public static readonly ValueConverter<SurveyQuestionId, Guid> QuestionKey = new(id => id.Value, value => SurveyQuestionId.From(value));

    /// <summary>Les choix d'une question, dans leur ordre, en tableau JSON.</summary>
    public static readonly ValueConverter<List<string>, string> Choices = new(
        choices => JsonSerializer.Serialize(choices, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly ValueComparer<List<string>> ChoicesComparer = new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    /// <summary>Contrainte CHECK « colonne IN (valeurs de l'enum) ».</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        column + " IN (" + string.Join(", ", Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";
}

internal sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    /// <summary>Un seul avis par commande : garde-fou derrière le contrôle fait avant d'écrire (deux envois simultanés).</summary>
    public const string OrderIndex = "ix_feedbacks_tenant_id_order_id";

    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedbacks", table =>
        {
            table.HasCheckConstraint("ck_feedbacks_rating", "rating BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_feedbacks_status", ReviewsConverters.In<FeedbackStatus>("status"));
            // Examiné : par qui et quand, ensemble.
            table.HasCheckConstraint("ck_feedbacks_reviewed", "(reviewed_by IS NULL) = (reviewed_at_utc IS NULL)");
            // Un avis détaché n'a plus ni auteur ni commentaire.
            table.HasCheckConstraint("ck_feedbacks_detached", "customer_detached_at_utc IS NULL OR (customer_id IS NULL AND comment IS NULL)");
            // Un avis archivé n'est plus publié.
            table.HasCheckConstraint("ck_feedbacks_published", "published_at_utc IS NULL OR status <> " + ((int)FeedbackStatus.Archived).ToString(CultureInfo.InvariantCulture));
        });

        builder.HasKey(feedback => feedback.Id);
        builder.Property(feedback => feedback.Id).HasColumnName("id").HasConversion(ReviewsConverters.FeedbackKey).ValueGeneratedNever();
        builder.Property(feedback => feedback.TenantId).HasColumnName("tenant_id").HasConversion(ReviewsConverters.TenantKey);
        builder.Property(feedback => feedback.LocationId).HasColumnName("location_id").HasConversion(ReviewsConverters.LocationKey);
        builder.Property(feedback => feedback.CustomerId).HasColumnName("customer_id").HasConversion(ReviewsConverters.CustomerKey);
        builder.Property(feedback => feedback.OrderId).HasColumnName("order_id").HasConversion(ReviewsConverters.OrderKey);
        // La note en une colonne entière : filtrable et agrégeable en base (moyenne, répartition).
        builder.ComplexProperty(feedback => feedback.Rating, rating => rating.Property(r => r.Value).HasColumnName("rating"));
        builder.Property(feedback => feedback.Comment).HasColumnName("comment").HasMaxLength(Feedback.CommentMaxLength);
        // Numérique : les valeurs de l'enum sont explicites et stables.
        builder.Property(feedback => feedback.Status).HasColumnName("status");
        builder.Property(feedback => feedback.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(feedback => feedback.ReviewedBy).HasColumnName("reviewed_by").HasConversion(ReviewsConverters.UserKey);
        builder.Property(feedback => feedback.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(feedback => feedback.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(feedback => feedback.CustomerDetachedAtUtc).HasColumnName("customer_detached_at_utc");
        builder.Property(feedback => feedback.PublishedAtUtc).HasColumnName("published_at_utc");

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL (examen et archivage simultanés...).
        builder.Property<uint>("Version").IsRowVersion();

        // Listes et statistiques : par restaurant (et établissement), des plus récents aux plus anciens.
        builder.HasIndex(feedback => new { feedback.TenantId, feedback.SubmittedAtUtc })
            .HasDatabaseName("ix_feedbacks_tenant_id_submitted_at_utc");

        // Les avis affichés par le site et l'application du restaurant.
        builder.HasIndex(feedback => new { feedback.TenantId, feedback.SubmittedAtUtc }, "ix_feedbacks_tenant_id_published")
            .HasFilter("published_at_utc IS NOT NULL");
        builder.HasIndex(feedback => new { feedback.TenantId, feedback.LocationId, feedback.SubmittedAtUtc })
            .HasDatabaseName("ix_feedbacks_tenant_id_location_id_submitted_at_utc");
        // L'effacement d'un client retrouve ses avis.
        builder.HasIndex(feedback => new { feedback.TenantId, feedback.CustomerId })
            .HasFilter("customer_id IS NOT NULL")
            .HasDatabaseName("ix_feedbacks_tenant_id_customer_id");
        builder.HasIndex(feedback => new { feedback.TenantId, feedback.OrderId })
            .IsUnique()
            .HasFilter("order_id IS NOT NULL")
            .HasDatabaseName(OrderIndex);

        builder.Ignore(feedback => feedback.DomainEvents);
        builder.Ignore(feedback => feedback.IsCustomerDetached);
        builder.Ignore(feedback => feedback.IsPublished);
    }
}

internal sealed class SurveyConfiguration : IEntityTypeConfiguration<Survey>
{
    public const string OwnerProperty = "SurveyId";

    public void Configure(EntityTypeBuilder<Survey> builder)
    {
        builder.ToTable("surveys", table =>
        {
            table.HasCheckConstraint("ck_surveys_status", ReviewsConverters.In<SurveyStatus>("status"));
            table.HasCheckConstraint("ck_surveys_published", "status = 1 OR published_at_utc IS NOT NULL");
            table.HasCheckConstraint("ck_surveys_closed", "(status = 3) = (closed_at_utc IS NOT NULL)");
        });

        builder.HasKey(survey => survey.Id);
        builder.Property(survey => survey.Id).HasColumnName("id").HasConversion(ReviewsConverters.SurveyKey).ValueGeneratedNever();
        builder.Property(survey => survey.TenantId).HasColumnName("tenant_id").HasConversion(ReviewsConverters.TenantKey);
        builder.Property(survey => survey.Title).HasColumnName("title").HasMaxLength(Survey.TitleMaxLength);
        builder.Property(survey => survey.Status).HasColumnName("status");
        builder.Property(survey => survey.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(survey => survey.ClosedAtUtc).HasColumnName("closed_at_utc");

        // Concurrence optimiste sur xmin ; l'unit of work réécrit le questionnaire quand seules ses questions changent.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(survey => survey.Questions)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(survey => survey.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(survey => new { survey.TenantId, survey.Status }).HasDatabaseName("ix_surveys_tenant_id_status");

        builder.Ignore(survey => survey.DomainEvents);
    }
}

internal sealed class SurveyQuestionConfiguration : IEntityTypeConfiguration<SurveyQuestion>
{
    public void Configure(EntityTypeBuilder<SurveyQuestion> builder)
    {
        builder.ToTable("survey_questions", table =>
        {
            table.HasCheckConstraint("ck_survey_questions_type", ReviewsConverters.In<SurveyQuestionType>("type"));
            table.HasCheckConstraint("ck_survey_questions_position", "position >= 1");
        });

        builder.HasKey(question => question.Id);
        // Jamais généré : une question atteinte via son questionnaire avec un id déjà défini est une NOUVELLE ligne.
        builder.Property(question => question.Id).HasColumnName("id").HasConversion(ReviewsConverters.QuestionKey).ValueGeneratedNever();
        builder.Property<SurveyId>(SurveyConfiguration.OwnerProperty).HasColumnName("survey_id").HasConversion(ReviewsConverters.SurveyKey);
        builder.Property(question => question.Text).HasColumnName("text").HasMaxLength(SurveyQuestion.TextMaxLength);
        builder.Property(question => question.Type).HasColumnName("type");
        builder.Property(question => question.IsRequired).HasColumnName("is_required");
        builder.Property(question => question.Order).HasColumnName("position");
        builder.Property<List<string>>("_choices")
            .HasColumnName("choices")
            .HasColumnType("jsonb")
            .HasConversion(ReviewsConverters.Choices, ReviewsConverters.ChoicesComparer);

        // Positions sans trou ni doublon : vérifiées par le domaine, pas d'index unique (un déplacement les réécrit toutes).
        builder.HasIndex(SurveyConfiguration.OwnerProperty).HasDatabaseName("ix_survey_questions_survey_id");

        builder.Ignore(question => question.Choices);
        builder.Ignore(question => question.IsChoiceQuestion);
    }
}

/// <summary>
/// Réponses aux questionnaires (<c>survey_responses</c>) : réponses en jsonb, aucun client. Une réponse au plus par
/// commande et par questionnaire (index unique partiel).
/// </summary>
internal sealed class SurveyResponseConfiguration : IEntityTypeConfiguration<SurveyResponse>
{
    public const string OrderIndex = "ix_survey_responses_tenant_id_survey_id_order_id";

    public void Configure(EntityTypeBuilder<SurveyResponse> builder)
    {
        builder.ToTable("survey_responses", table => table.HasCheckConstraint("ck_survey_responses_answers", "jsonb_typeof(answers) = 'array'"));
        builder.HasKey(response => response.Id);
        builder.Property(response => response.Id).HasColumnName("id").HasConversion(id => id.Value, value => SurveyResponseId.From(value)).ValueGeneratedNever();
        builder.Property(response => response.TenantId).HasColumnName("tenant_id").HasConversion(ReviewsConverters.TenantKey);
        builder.Property(response => response.SurveyId).HasColumnName("survey_id").HasConversion(ReviewsConverters.SurveyKey);
        builder.Property(response => response.LocationId).HasColumnName("location_id").HasConversion(ReviewsConverters.LocationKey);
        builder.Property(response => response.OrderId).HasColumnName("order_id").HasConversion(ReviewsConverters.OrderKey);
        builder.Property(response => response.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property<List<SurveyAnswer>>("_answers")
            .HasColumnName("answers")
            .HasColumnType("jsonb")
            .HasConversion(SurveyAnswers.Converter, SurveyAnswers.Comparer);
        builder.Ignore(response => response.Answers);
        builder.HasIndex(response => new { response.TenantId, response.SurveyId, response.SubmittedAtUtc })
            .HasDatabaseName("ix_survey_responses_tenant_id_survey_id_submitted_at_utc");
        builder.HasIndex(response => new { response.TenantId, response.SurveyId, response.OrderId })
            .IsUnique()
            .HasFilter("order_id IS NOT NULL")
            .HasDatabaseName(OrderIndex);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(response => response.DomainEvents);
    }
}

internal static class SurveyAnswers
{
    public static readonly ValueConverter<List<SurveyAnswer>, string> Converter = new(answers => Write(answers), json => Read(json));

    public static readonly ValueComparer<List<SurveyAnswer>> Comparer = new(
        (left, right) => Write(left!) == Write(right!),
        answers => Write(answers).GetHashCode(StringComparison.Ordinal),
        answers => answers.ToList());

    private static string Write(List<SurveyAnswer> answers) =>
        JsonSerializer.Serialize(answers.Select(answer => new Row(answer.QuestionId.Value, answer.Rating, answer.Text, answer.Choices.Count == 0 ? null : answer.Choices.ToList())).ToList());

    private static List<SurveyAnswer> Read(string json) =>
        (JsonSerializer.Deserialize<List<Row>>(json) ?? [])
            .Select(row => new SurveyAnswer(SurveyQuestionId.From(row.Question), row.Rating, row.Text, row.Choices ?? []))
            .ToList();

    private sealed record Row(
        [property: System.Text.Json.Serialization.JsonPropertyName("q")] Guid Question,
        [property: System.Text.Json.Serialization.JsonPropertyName("r")] int? Rating,
        [property: System.Text.Json.Serialization.JsonPropertyName("t")] string? Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("c")] List<string>? Choices);
}
