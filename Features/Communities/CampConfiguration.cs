using System.Text.Json;
using System.Text.Json.Serialization;
using oyinQ.Bot.Data.Entities;

namespace oyinQ.Bot.Features.Communities;

public sealed record CampFieldOption(string Id, string Label, decimal Amount = 0);
public sealed record CampRegistrationField(string Id, string Label, string Type, bool Required = false,
    string? Hint = null, IReadOnlyList<CampFieldOption>? Options = null, decimal Amount = 0, bool PerDay = false);
public sealed record CampPricing(string Currency = "KZT", decimal Amount = 0, bool PerDay = false,
    decimal? EarlyAmount = null, DateOnly? EarlyUntil = null, decimal AccommodationAmount = 0,
    bool AccommodationPerDay = false);
public sealed record CampConfiguration(int Version = 1, string? Description = null, string? LocationName = null,
    string? LocationUrl = null, string? PaymentInstructions = null, CampPricing? Pricing = null,
    IReadOnlyList<CampRegistrationField>? RegistrationFields = null);
public sealed record CampQuoteLine(string Label, int Quantity, decimal UnitAmount, decimal Amount);
public sealed record CampRegistrationQuote(string Currency, decimal Total, IReadOnlyList<CampQuoteLine> Lines,
    DateTimeOffset CalculatedAtUtc);
public sealed record CampRegistrationData(int Version = 1, IReadOnlyDictionary<string, string>? Answers = null,
    CampRegistrationQuote? Quote = null);

public static class CampConfigurationRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static CampConfiguration Read(string? json) => string.IsNullOrWhiteSpace(json)
        ? Normalize(null) : Normalize(JsonSerializer.Deserialize<CampConfiguration>(json, Json));
    public static CampRegistrationData ReadRegistration(string? json) => string.IsNullOrWhiteSpace(json)
        ? new(Answers: new Dictionary<string, string>()) : JsonSerializer.Deserialize<CampRegistrationData>(json, Json) ?? new();

    public static CampConfiguration Normalize(CampConfiguration? configuration)
    {
        var value = configuration ?? new();
        if (value.Version != 1) throw new ArgumentException("Версия настроек кэмпа не поддерживается.");
        var fields = value.RegistrationFields ?? [];
        if (fields.Count > 20) throw new ArgumentException("Можно добавить не больше 20 вопросов.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<CampRegistrationField>();
        foreach (var field in fields)
        {
            if (field is null) throw new ArgumentException("Вопрос не должен быть пустым.");
            RequireId(field.Id);
            if (!ids.Add(field.Id)) throw new ArgumentException("Вопросы должны иметь разные идентификаторы.");
            if (field.Type is not ("Text" or "Multiline" or "Choice" or "Checkbox"))
                throw new ArgumentException("Выберите поддерживаемый тип вопроса.");
            var options = field.Options ?? [];
            if (field.Type == "Choice" && (options.Count < 2 || options.Count > 32))
                throw new ArgumentException("У вопроса с выбором должно быть от 2 до 32 вариантов.");
            if (field.Type != "Choice" && options.Count > 0) throw new ArgumentException("Варианты доступны только для вопроса с выбором.");
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            var optionLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalizedOptions = options.Select(option =>
            {
                if (option is null) throw new ArgumentException("Вариант ответа не должен быть пустым.");
                RequireId(option.Id);
                var label = Text(option.Label, 120, true)!;
                if (!optionIds.Add(option.Id) || !optionLabels.Add(label))
                    throw new ArgumentException("Варианты ответа не должны повторяться.");
                return option with { Label = label, Amount = Money(option.Amount) };
            }).ToArray();
            if (field.Type != "Checkbox" && field.Amount != 0)
                throw new ArgumentException("Доплата за отметку доступна только для флажка.");
            if (field.Type is "Text" or "Multiline" && field.PerDay)
                throw new ArgumentException("Текстовые вопросы не влияют на стоимость.");
            normalized.Add(field with { Label = Text(field.Label, 120, true)!, Hint = Text(field.Hint, 400),
                Options = normalizedOptions, Amount = Money(field.Amount) });
        }
        var pricing = value.Pricing;
        if (pricing is not null)
        {
            var currency = pricing.Currency?.Trim().ToUpperInvariant();
            if (currency is not ("KZT" or "RUB" or "USD" or "EUR" or "KGS" or "UZS"))
                throw new ArgumentException("Выберите валюту из списка.");
            if ((pricing.EarlyAmount is null) != (pricing.EarlyUntil is null))
                throw new ArgumentException("Для ранней цены укажите сумму и последний день её действия.");
            pricing = pricing with { Currency = currency, Amount = Money(pricing.Amount),
                EarlyAmount = pricing.EarlyAmount is { } early ? Money(early) : null,
                AccommodationAmount = Money(pricing.AccommodationAmount) };
        }
        if (pricing is null && normalized.Any(field => field.Amount > 0 || field.Options!.Any(option => option.Amount > 0)))
            throw new ArgumentException("Для расчёта доплат сначала настройте стоимость и валюту.");
        var locationUrl = Text(value.LocationUrl, 1000);
        if (locationUrl is not null && (!Uri.TryCreate(locationUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)))
            throw new ArgumentException("Ссылка на место должна начинаться с https://.");
        return value with { Description = Text(value.Description, 6000), LocationName = Text(value.LocationName, 200),
            LocationUrl = locationUrl, PaymentInstructions = Text(value.PaymentInstructions, 2000),
            Pricing = pricing, RegistrationFields = normalized };
    }

    public static IReadOnlyDictionary<string, string> ValidateAnswers(CampConfiguration config,
        IReadOnlyDictionary<string, string>? answers, bool requireAll = true)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var fields = config.RegistrationFields ?? [];
        if (answers?.Keys.Any(key => fields.All(field => field.Id != key)) == true)
            throw new ArgumentException("Ответ относится к неизвестному вопросу.");
        foreach (var field in fields)
        {
            var value = answers?.GetValueOrDefault(field.Id)?.Trim() ?? "";
            if (requireAll && field.Required && (value.Length == 0 || field.Type == "Checkbox" && value != "true"))
                throw new ArgumentException($"Заполните «{field.Label}».");
            if (value.Length == 0) continue;
            if (field.Type == "Checkbox" && value is not ("true" or "false"))
                throw new ArgumentException($"Выберите ответ для «{field.Label}».");
            if (field.Type == "Choice" && field.Options!.All(option => option.Id != value))
                throw new ArgumentException($"Выберите вариант из списка для «{field.Label}».");
            if (value.Length > (field.Type == "Multiline" ? 2000 : 400))
                throw new ArgumentException($"Ответ на «{field.Label}» слишком длинный.");
            result[field.Id] = value;
        }
        return result;
    }

    public static CampRegistrationQuote? Quote(CampConfiguration config, IReadOnlyDictionary<string, string> answers,
        int days, string zone, DateTimeOffset now, bool needsAccommodation = false)
    {
        if (config.Pricing is not { } price) return null;
        var lines = new List<CampQuoteLine>();
        var early = price.EarlyUntil is { } until && CommunityTime.LocalDate(now, zone) <= until;
        Add(early ? "Участие · ранняя цена" : "Участие", early ? price.EarlyAmount!.Value : price.Amount, price.PerDay);
        if (needsAccommodation && price.AccommodationAmount > 0)
            Add("Жильё", price.AccommodationAmount, price.AccommodationPerDay);
        foreach (var field in config.RegistrationFields ?? [])
        {
            var value = answers.GetValueOrDefault(field.Id);
            if (field.Type == "Checkbox" && value == "true" && field.Amount > 0) Add(field.Label, field.Amount, field.PerDay);
            if (field.Type == "Choice" && field.Options!.SingleOrDefault(option => option.Id == value) is { Amount: > 0 } option)
                Add($"{field.Label}: {option.Label}", option.Amount, field.PerDay);
        }
        return new(price.Currency, lines.Sum(line => line.Amount), lines, now);
        void Add(string label, decimal amount, bool perDay)
        {
            var quantity = perDay ? days : 1;
            lines.Add(new(label, quantity, amount, quantity * amount));
        }
    }

    public static CampRegistrationQuote? QuoteForRegistration(CampConfiguration config,
        IReadOnlyDictionary<string, string> answers, IReadOnlyCollection<DateOnly> dates,
        string zone, DateTimeOffset now, CampRegistration? registration, bool needsAccommodation = false)
    {
        var saved = ReadRegistration(registration?.RegistrationDataJson);
        var unchanged = registration is not null
            && registration.NeedsAccommodation == needsAccommodation
            && registration.SelectedDays.Select(x => x.Date).ToHashSet().SetEquals(dates)
            && answers.Count == (saved.Answers?.Count ?? 0)
            && answers.All(pair => saved.Answers?.GetValueOrDefault(pair.Key) == pair.Value);
        return unchanged && saved.Quote is not null ? saved.Quote : Quote(config, answers, dates.Count, zone, now, needsAccommodation);
    }

    public static IReadOnlyDictionary<string, string> PresentAnswers(CampConfiguration config, CampRegistrationData data) =>
        (config.RegistrationFields ?? []).ToDictionary(field => field.Id, field =>
        {
            var value = data.Answers?.GetValueOrDefault(field.Id);
            return string.IsNullOrEmpty(value) ? "Не указано" : field.Type switch
            {
                "Choice" => field.Options!.SingleOrDefault(option => option.Id == value)?.Label ?? value,
                "Checkbox" => value == "true" ? "Да" : "Нет",
                _ => value
            };
        });

    public static void EnsureFieldsUnchanged(CampConfiguration before, CampConfiguration after, bool hasRegistrations)
    {
        if (!hasRegistrations) return;
        var existing = before.RegistrationFields ?? [];
        var updated = after.RegistrationFields ?? [];
        if (updated.Count < existing.Count ||
            Serialize(existing) != Serialize(updated.Take(existing.Count).ToArray()) ||
            updated.Skip(existing.Count).Any(field => field.Required))
            throw new InvalidOperationException("После первой регистрации можно добавлять только необязательные вопросы. Существующие вопросы, варианты и доплаты менять нельзя.");
    }
    private static void RequireId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Идентификатор вопроса или варианта некорректен.");
    }
    private static decimal Money(decimal value)
    {
        if (value < 0 || value > 10000000 || decimal.Round(value, 2) != value)
            throw new ArgumentException("Сумма должна быть от 0 до 10 000 000 и содержать не больше двух знаков после запятой.");
        return value;
    }
    private static string? Text(string? value, int max, bool required = false)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (required) throw new ArgumentException("Укажите название вопроса или варианта.");
            return null;
        }
        if (text.Length > max) throw new ArgumentException($"Текст не должен быть длиннее {max} символов.");
        return text;
    }
}
