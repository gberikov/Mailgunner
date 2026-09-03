# Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Устранить все находки код-ревью от 2026-09-02: четыре блокера (webhooks path, `created_at`, JSON-тело suppressions, двойная регистрация), дыры безопасности, слабые места retry, DX-пробелы, малые пробелы покрытия API и готовность к релизу `0.2.0`.

**Architecture:** Библиотека `src/Mailgunner` (net8.0 + netstandard2.0) с типизированным `HttpClient`, Polly v8 resilience-хендлером, source-gen JSON и офлайн xUnit-тестами на `StubHttpMessageHandler`. Изменения точечные: сначала блокеры, затем один общий HTTP-хелпер, затем безопасность/retry/DX поверх него, в конце инфраструктура релиза.

**Tech Stack:** C# (LangVersion latest, Nullable, TreatWarningsAsErrors), .NET SDK 10 (`global.json`), xUnit 2.9, Polly.Core 8.x, System.Text.Json, Microsoft.Extensions.Http, MinVer, GitHub Actions.

**Spec:** Итоги код-ревью в этой сессии (сообщение с разделами «Блокеры перед релизом», «По областям фокуса», «Вердикт»). Отдельного spec-файла нет; каждая задача ниже содержит требование целиком.

## Global Constraints

- Оба TFM (`net8.0;netstandard2.0`) обязаны собираться без предупреждений: `TreatWarningsAsErrors=true`, `AnalysisMode=Recommended`, XML-доки на всех public-членах (`CS1591` = error), file-scoped namespaces, `IDE0005` (лишние using) = error в `src/`.
- На netstandard2.0 недоступны: `string.Contains(char)`, `HttpRequestMessage.Options`, `Enum.IsDefined<T>()`, `ReadAsStringAsync(CancellationToken)`, `ArgumentNullException.ThrowIfNull`. Используй `#if NET8_0_OR_GREATER` как в существующем коде (`Guard.cs`, `MailgunnerClient.cs`).
- Тесты офлайн, без сети и без секретов. Стиль тестов: `snake_case`-имена методов, паттерн `BuildClient()` с `StubHttpMessageHandler` (см. `tests/Mailgunner.Tests/Sending/SendMessageTests.cs`).
- Команды shell префиксуй `rtk` (`rtk dotnet build`, `rtk git commit`). Ветка по умолчанию `master`.
- Каждый коммит завершай трейлерами:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01VrPcBAaiVSR7PwbtSr7yXt
  ```
- Публичный API до `1.0`: допускаются аддитивные изменения и изменение nullability; удаление/переименование public-членов запрещено.
- Полная проверка после каждой задачи: `rtk dotnet build Mailgunner.slnx -c Release` и `rtk dotnet test Mailgunner.slnx -c Release --no-build`. Ожидание: 0 warnings, все тесты зелёные.
- CHANGELOG: каждое изменение public-поведения добавляй строкой в `CHANGELOG.md` секцию `## [Unreleased]` (формат Keep a Changelog: `### Added` / `### Changed` / `### Fixed` / `### Security`). Финальная нарезка версии в Task 24.

## Порядок фаз

| Фаза | Задачи | Зачем в таком порядке |
|------|--------|------------------------|
| 1. Блокеры | 1–4 | Ломают прод, малые независимые диффы |
| 2. База | 5–6 | Зависимости и общий HTTP-хелпер, на которых строятся остальные задачи |
| 3. Безопасность | 7–9 | Инъекция адресов, экранирование домена, ретраи отправок |
| 4. Устойчивость и ошибки | 10–12 | Таймаут попытки, границы retry, информативные исключения |
| 5. DX и покрытие API | 13–18 | Reply-To, опции отправки, inline-батч, `accepted`, Stream-вложения, freshness подписи |
| 6. Релиз | 19–24 | net48-тесты, release gate, интеграционные тесты, чистка имён, README/CHANGELOG |

Крупные пробелы покрытия API (mailing lists, Events API, templates CRUD, whitelists, v4 webhooks, `messages.mime`, stored messages, типизированный payload вебхука) в этот план **не входят**: каждому нужен свой spec. Список в конце файла.

---

## Фаза 1. Блокеры

### Task 1: Webhooks — правильный путь `/v3/domains/{domain}/webhooks`

**Files:**
- Modify: `src/Mailgunner/Internal/MailgunWebhooks.cs:157-161`
- Modify: `tests/Mailgunner.Tests/WebhookManagement/WebhookRoutingTests.cs:17,40`
- Modify: любые другие тесты в `tests/Mailgunner.Tests/WebhookManagement/`, где встречается `/webhooks` в `AbsolutePath`

**Interfaces:**
- Consumes: `MailgunWebhooks.RootUri()`, `MailgunWebhooks.ItemUri(WebhookEventType)`
- Produces: те же методы, но с путём `v3/domains/{domain}/webhooks[/{token}]`

- [ ] **Step 1: Обновить существующие тесты маршрутизации на правильный путь**

В `tests/Mailgunner.Tests/WebhookManagement/WebhookRoutingTests.cs` заменить:

```csharp
// строка 17
Assert.Equal($"/v3/domains/{WebhookHarness.Domain}/webhooks", stub.LastRequestUri!.AbsolutePath);
// строка 40
Assert.Equal("/v3/domains/other.example.org/webhooks/delivered", stub.LastRequestUri!.AbsolutePath);
```

Затем найти остальные проверки пути:

```bash
rtk grep -rn "/webhooks" tests/Mailgunner.Tests/WebhookManagement/
```

Во всех найденных строках вида `$"/v3/{...}/webhooks..."` вставить сегмент `domains/` после `/v3/`.

- [ ] **Step 2: Запустить тесты, убедиться что они падают**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~WebhookManagement" -v q`
Expected: FAIL, ожидаемый путь содержит `/v3/domains/`, фактический нет.

- [ ] **Step 3: Исправить построение URI**

В `src/Mailgunner/Internal/MailgunWebhooks.cs` заменить два метода:

```csharp
    private System.Uri RootUri() =>
        new System.Uri($"v3/domains/{_domain}/webhooks", System.UriKind.Relative);

    private System.Uri ItemUri(WebhookEventType eventType) =>
        new System.Uri($"v3/domains/{_domain}/webhooks/{WebhookEventTypes.ToToken(eventType)}", System.UriKind.Relative);
```

В XML-комментарии класса и в `IMailgunWebhooks.cs` заменить упоминание `/v3/{domain}/webhooks` на `/v3/domains/{domain}/webhooks`. То же в `CHANGELOG.md` (секция Unreleased, пункт про webhook management) и `README.md`, если путь там упомянут.

- [ ] **Step 4: Запустить тесты, убедиться что проходят**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~WebhookManagement" -v q`
Expected: PASS.

- [ ] **Step 5: CHANGELOG и коммит**

В `CHANGELOG.md` под `## [Unreleased]` добавить секцию `### Fixed` (если нет) со строкой:

```
- Domain webhook management now targets Mailgun's actual path `/v3/domains/{domain}/webhooks`; the previous `/v3/{domain}/webhooks` returned HTTP 404 for every operation.
```

```bash
rtk git add src/Mailgunner/Internal/MailgunWebhooks.cs src/Mailgunner/IMailgunWebhooks.cs tests/Mailgunner.Tests/WebhookManagement CHANGELOG.md README.md
rtk git commit -m "fix: route domain webhook operations to /v3/domains/{domain}/webhooks"
```

---

### Task 2: Suppressions — парсинг `created_at` с суффиксом `UTC`

**Files:**
- Modify: `src/Mailgunner/Internal/MailgunSuppressions.cs:93-113` (класс `SuppressionTime`)
- Test: `tests/Mailgunner.Tests/Suppressions/SuppressionModelTests.cs`

**Interfaces:**
- Consumes: `SuppressionTime.Parse(string?)` → `DateTimeOffset?`
- Produces: тот же метод; распознаёт `ddd, dd MMM yyyy HH:mm:ss UTC`, старые форматы (`GMT`, числовой offset) сохраняются

- [ ] **Step 1: Написать падающий тест**

В `tests/Mailgunner.Tests/Suppressions/SuppressionModelTests.cs` добавить:

```csharp
    [Fact]
    public async Task Created_at_with_mailgun_utc_suffix_is_parsed()
    {
        // Mailgun's real wire format ends in "UTC", which the general .NET parser rejects.
        var client = BuildClient(
            "{\"items\":[{\"address\":\"a@x.com\",\"created_at\":\"Thu, 11 Dec 2025 01:49:40 UTC\"}],\"paging\":{}}");

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Equal(new DateTimeOffset(2025, 12, 11, 1, 49, 40, TimeSpan.Zero), Assert.Single(page.Items).CreatedAt);
    }
```

- [ ] **Step 2: Убедиться что тест падает**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "Created_at_with_mailgun_utc_suffix_is_parsed" -v q`
Expected: FAIL, `CreatedAt` равен `null`.

- [ ] **Step 3: Реализовать точный формат с запасным общим парсером**

Заменить тело `SuppressionTime.Parse`:

```csharp
internal static class SuppressionTime
{
    /// <summary>Mailgun's documented <c>created_at</c> shape, e.g. <c>Thu, 11 Dec 2025 01:49:40 UTC</c>.</summary>
    private const string MailgunFormat = "ddd, dd MMM yyyy HH:mm:ss 'UTC'";

    public static System.DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.DateTimeStyles styles =
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;

        if (System.DateTimeOffset.TryParseExact(value, MailgunFormat, inv, styles, out var exact))
        {
            return exact;
        }

        // Fallback for RFC 1123 ("GMT") and numeric-offset variants.
        return System.DateTimeOffset.TryParse(value, inv, styles, out var general) ? general : null;
    }
}
```

- [ ] **Step 4: Прогнать тесты suppressions**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~Suppressions" -v q`
Expected: PASS (включая старые тесты с `GMT` и `Unparseable_created_at_yields_null_and_does_not_throw`).

- [ ] **Step 5: CHANGELOG и коммит**

`### Fixed`: `- Suppression entries' CreatedAt is now populated: Mailgun's "…UTC" timestamps were silently parsed to null.`

```bash
rtk git add src/Mailgunner/Internal/MailgunSuppressions.cs tests/Mailgunner.Tests/Suppressions/SuppressionModelTests.cs CHANGELOG.md
rtk git commit -m "fix: parse Mailgun 'UTC'-suffixed created_at timestamps in suppression lists"
```

---

### Task 3: Suppressions — JSON-массив в `AddAsync` и новый `AddRangeAsync`

Документация Mailgun: тело `application/json` для `POST /v3/{domain}/{bounces|unsubscribes|complaints}` это **массив** до 1000 записей; одиночная запись принимается только как form-data. Делаем `AddAsync` частным случаем `AddRangeAsync`.

**Files:**
- Modify: `src/Mailgunner/ISuppressionList.cs`
- Modify: `src/Mailgunner/Internal/MailgunSuppressionList.cs`
- Modify: `src/Mailgunner/Internal/MailgunSuppressions.cs:16-32`
- Modify: `src/Mailgunner/Internal/SuppressionJsonContext.cs`
- Modify: `src/Mailgunner/Internal/MailgunBatchContent.cs:67-81` (сделать `Chunk` generic)
- Test: `tests/Mailgunner.Tests/Suppressions/SuppressionAddTests.cs`

**Interfaces:**
- Produces: `Task ISuppressionList<TEntry>.AddRangeAsync(IEnumerable<TEntry> entries, CancellationToken cancellationToken = default)`; `MailgunBatchContent.Chunk<T>(IList<T> items, int size)`; конструктор `MailgunSuppressionList` принимает `JsonTypeInfo<List<TAddDto>> addTypeInfo`.

- [ ] **Step 1: Написать падающие тесты**

В `SuppressionAddTests.cs` заменить тест `Add_bounce_posts_json_to_the_bounces_endpoint_with_address_code_and_error` (проверка формы тела) и добавить два новых:

```csharp
    [Fact]
    public async Task Add_bounce_posts_a_single_element_json_array()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Bounces.AddAsync(
            new Bounce { Address = "a@x.com", Code = "550", Error = "Mailbox full" });

        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal($"/v3/{Domain}/bounces", stub.LastRequestUri!.AbsolutePath);
        Assert.Equal("application/json", stub.LastContentMediaType);
        Assert.Equal(
            "[{\"address\":\"a@x.com\",\"code\":\"550\",\"error\":\"Mailbox full\"}]",
            stub.LastBody);
    }

    [Fact]
    public async Task AddRange_posts_all_entries_in_one_json_array()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Complaints.AddRangeAsync(new[]
        {
            new Complaint { Address = "a@x.com" },
            new Complaint { Address = "b@x.com" },
        });

        Assert.Single(stub.Requests);
        Assert.Equal("[{\"address\":\"a@x.com\"},{\"address\":\"b@x.com\"}]", stub.LastBody);
    }

    [Fact]
    public async Task AddRange_splits_more_than_1000_entries_into_multiple_requests()
    {
        var (client, stub) = BuildClient();
        var entries = Enumerable.Range(0, 1500).Select(i => new Complaint { Address = $"u{i}@x.com" }).ToList();

        await client.Suppressions.Complaints.AddRangeAsync(entries);

        Assert.Equal(2, stub.Requests.Count);
        Assert.Contains("\"u999@x.com\"", stub.Requests[0].Body);
        Assert.Contains("\"u1000@x.com\"", stub.Requests[1].Body);
    }

    [Fact]
    public async Task AddRange_with_a_blank_address_throws_before_any_request()
    {
        var (client, stub) = BuildClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.Suppressions.Bounces.AddRangeAsync(
            new[] { new Bounce { Address = "a@x.com" }, new Bounce { Address = " " } }));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task AddRange_with_no_entries_issues_no_request()
    {
        var (client, stub) = BuildClient();

        await client.Suppressions.Bounces.AddRangeAsync(Array.Empty<Bounce>());

        Assert.Empty(stub.Requests);
    }
```

- [ ] **Step 2: Убедиться что тесты не компилируются / падают**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~SuppressionAddTests" -v q`
Expected: ошибка компиляции `AddRangeAsync` не найден.

- [ ] **Step 3: Расширить интерфейс**

В `src/Mailgunner/ISuppressionList.cs` после `AddAsync` добавить:

```csharp
    /// <summary>
    /// Adds several entries to the list. Entries are sent as a JSON array in requests of at most 1000
    /// entries each (Mailgun's per-request limit), issued sequentially in order; the first non-success
    /// response throws and stops the remaining requests (entries already accepted are not rolled back).
    /// An empty sequence is a no-op.
    /// </summary>
    /// <param name="entries">The entries to add; every address must be non-blank.</param>
    /// <param name="cancellationToken">A token that cancels the operation; honored between requests.</param>
    /// <returns>A task that completes when every request has been accepted.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">An entry is <see langword="null"/> or has a blank address. Thrown before any request.</exception>
    /// <exception cref="MailgunnerException">A request returned a non-success response.</exception>
    System.Threading.Tasks.Task AddRangeAsync(
        System.Collections.Generic.IEnumerable<TEntry> entries,
        System.Threading.CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Сделать `Chunk` generic**

В `src/Mailgunner/Internal/MailgunBatchContent.cs` заменить сигнатуру и тело:

```csharp
    public static System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyList<T>> Chunk<T>(
        System.Collections.Generic.IList<T> items, int size)
    {
        for (var start = 0; start < items.Count; start += size)
        {
            var end = System.Math.Min(start + size, items.Count);
            var slice = new System.Collections.Generic.List<T>(end - start);
            for (var i = start; i < end; i++)
            {
                slice.Add(items[i]);
            }

            yield return slice;
        }
    }
```

Вызов в `MailgunnerClient.SendBatchAsync` не меняется (вывод типа).

- [ ] **Step 5: Зарегистрировать списки в JSON-контексте**

В `src/Mailgunner/Internal/SuppressionJsonContext.cs` заменить три атрибута `AddXxxDto` на списки:

```csharp
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<AddBounceDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<AddUnsubscribeDto>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(System.Collections.Generic.List<AddComplaintDto>))]
```

Сгенерированные свойства называются `ListAddBounceDto`, `ListAddUnsubscribeDto`, `ListAddComplaintDto`.

- [ ] **Step 6: Реализовать в `MailgunSuppressionList`**

Поле и параметр конструктора: `JsonTypeInfo<TAddDto> addTypeInfo` → `JsonTypeInfo<System.Collections.Generic.List<TAddDto>> addTypeInfo` (имя поля `_addTypeInfo` оставить). Заменить `AddAsync` и добавить `AddRangeAsync`:

```csharp
    private const int MaxAddPerRequest = 1000;

    /// <inheritdoc />
    public System.Threading.Tasks.Task AddAsync(
        TEntry entry,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (entry is null)
        {
            throw new System.ArgumentNullException(nameof(entry));
        }

        return AddRangeAsync(new[] { entry }, cancellationToken);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task AddRangeAsync(
        System.Collections.Generic.IEnumerable<TEntry> entries,
        System.Threading.CancellationToken cancellationToken = default)
    {
        Guard.NotNull(entries, nameof(entries));

        var bodies = new System.Collections.Generic.List<TAddDto>();
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(_addressOf(entry)))
            {
                throw new System.ArgumentException("Every entry must be non-null with a non-blank address.", nameof(entries));
            }

            bodies.Add(_toAddBody(entry));
        }

        foreach (var chunk in MailgunBatchContent.Chunk(bodies, MaxAddPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = System.Text.Json.JsonSerializer.Serialize(
                new System.Collections.Generic.List<TAddDto>(chunk), _addTypeInfo);
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, RootUri())
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };

            await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
```

В `MailgunSuppressions.cs` три вызова конструктора: `SuppressionJsonContext.Default.AddBounceDto` → `ListAddBounceDto`, аналогично `ListAddUnsubscribeDto`, `ListAddComplaintDto`.

Сообщение исключения для одиночного `AddAsync` с пустым адресом изменилось на общее; тест `Add_blank_address_throws_argument_exception_and_issues_no_request` проверяет только тип, он проходит.

- [ ] **Step 7: Прогнать сборку и все тесты**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build`
Expected: 0 warnings; PASS. Если старый тест `Add_bounce_posts_json_to_the_bounces_endpoint_with_address_code_and_error` остался, удалить его (заменён Step 1).

- [ ] **Step 8: README, CHANGELOG, коммит**

README, раздел «Suppression lists», после строки про `AddAsync` добавить: ``**`AddRangeAsync`** sends many entries as a JSON array (chunked by 1000 per request).``
CHANGELOG `### Fixed`: `- Suppression AddAsync now sends the JSON array shape Mailgun documents; a bare JSON object was rejected.` и `### Added`: `- ISuppressionList<T>.AddRangeAsync for bulk adds (chunks of 1000 per request).`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "fix: send suppression adds as a JSON array; add AddRangeAsync"
```

---

### Task 4: Повторный `AddMailgunner()` не должен дублировать retry-хендлер

Факт (проверен пробой): два вызова unnamed `AddMailgunner` дают два `MailgunResilienceHandler` в цепочке (`HttpMessageHandlerBuilderActions.Count == 2`), потому что `AddHttpMessageHandler` дописывает действие. Документация обещает «последний вызов побеждает».

**Files:**
- Modify: `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs:57-84`
- Test: `tests/Mailgunner.Tests/Registration/UnnamedReRegistrationTests.cs` (создать)

**Interfaces:**
- Produces: приватный `static bool IsUnnamedClientRegistered(IServiceCollection services)`.

- [ ] **Step 1: Написать падающие тесты**

Создать `tests/Mailgunner.Tests/Registration/UnnamedReRegistrationTests.cs`:

```csharp
using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mailgunner.Tests.Registration;

public class UnnamedReRegistrationTests
{
    [Fact]
    public void Registering_the_unnamed_client_twice_wires_a_single_resilience_handler()
    {
        var services = new ServiceCollection();
        services.AddMailgunner("a.example.com", "key-1", MailgunRegion.Us);
        services.AddMailgunner("b.example.com", "key-2", MailgunRegion.Eu);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(nameof(IMailgunnerClient));

        Assert.Single(options.HttpMessageHandlerBuilderActions);
    }

    [Fact]
    public async Task Registering_twice_does_not_multiply_retry_attempts()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}");
        var time = new RecordingTimeProvider();
        var services = new ServiceCollection();
        services.AddMailgunner(o => { o.Domain = "a.example.com"; o.SendingKey = "key-1"; o.Region = MailgunRegion.Us; o.Retry.MaxRetryAttempts = 1; })
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        services.AddMailgunner(o => { o.Domain = "b.example.com"; o.SendingKey = "key-2"; o.Region = MailgunRegion.Us; o.Retry.MaxRetryAttempts = 1; });
        services.AddSingleton<TimeProvider>(time);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();

        await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal(2, stub.Requests.Count); // 1 attempt + 1 retry, not (1+1)*(1+1)
        Assert.Equal("b.example.com", stub.LastRequestUri!.AbsolutePath.Split('/')[2]); // last options win
    }
}
```

- [ ] **Step 2: Убедиться что тесты падают**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~UnnamedReRegistrationTests" -v q`
Expected: первый тест FAIL (2 действия), второй FAIL (4 запроса).

- [ ] **Step 3: Реализовать защиту от повторной регистрации**

В `MailgunnerServiceCollectionExtensions.AddMailgunner(this IServiceCollection services, Action<MailgunnerOptions> configure)` после `services.TryAddTransient<MailgunResilienceHandler>();` и перед `return services.AddHttpClient<...>` вставить:

```csharp
        if (IsUnnamedClientRegistered(services))
        {
            // Re-registration: the typed client, its ConfigureHttpClient delegate (which reads the
            // options lazily, so the new settings apply) and the resilience handler are already wired.
            // Only the options changed above; return a builder for the same named client without
            // appending a second handler to the chain.
            return services.AddHttpClient(nameof(IMailgunnerClient));
        }
```

И добавить приватный метод:

```csharp
    /// <summary>Returns whether the unnamed typed client has already been wired into <paramref name="services"/>.</summary>
    private static bool IsUnnamedClientRegistered(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IMailgunnerClient))
            {
                return true;
            }
        }

        return false;
    }
```

Имя typed-клиента, которое M.E.Http выводит для `AddHttpClient<IMailgunnerClient, MailgunnerClient>`, равно `"IMailgunnerClient"` (`nameof(IMailgunnerClient)`), это подтверждено пробой.

- [ ] **Step 4: Прогнать тесты**

Run: `rtk dotnet test Mailgunner.slnx -c Release -v q`
Expected: PASS, в том числе `UnnamedBackwardCompatTests` и `NamedUnnamedIsolationTests`.

- [ ] **Step 5: CHANGELOG и коммит**

`### Fixed`: `- Calling the unnamed AddMailgunner more than once no longer stacks a second retry handler (which multiplied attempts and waits); the latest options still win.`

```bash
rtk git add src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs tests/Mailgunner.Tests/Registration/UnnamedReRegistrationTests.cs CHANGELOG.md
rtk git commit -m "fix: do not stack resilience handlers on repeated unnamed AddMailgunner"
```

---

## Фаза 2. База

### Task 5: Зависимости — `Polly.Core` и нижняя планка `8.0.x`

Библиотека с таргетом `net8.0` не должна тянуть потребителей на Extensions 10.x. `Polly` (метапакет с legacy v7 API) заменяется на `Polly.Core`: используются только `ResiliencePipelineBuilder`, `RetryStrategyOptions`, `ResilienceContextPool`, `Outcome<T>`, `ResiliencePropertyKey<T>`, все они в `Polly.Core`.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Mailgunner/Mailgunner.csproj:10-13`

- [ ] **Step 1: Обновить каталог версий**

В `Directory.Packages.props` заменить блоки runtime, sample и test-only зависимостей:

```xml
  <ItemGroup>
    <PackageVersion Include="System.Text.Json" Version="8.0.5" />
    <PackageVersion Include="Polly.Core" Version="8.7.0" />
    <PackageVersion Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
  </ItemGroup>
```

и в test stack: `<PackageVersion Include="Microsoft.Extensions.Configuration" Version="8.0.0" />`. Комментарий про «10.0.x train» удалить и заменить на: `<!-- Library floors stay on the 8.0.x train so net8.0 consumers are not forced onto newer Extensions packages. -->`.

Если `rtk dotnet list package --vulnerable --include-transitive` покажет уязвимость у выбранной 8.0.x версии, поднять до ближайшего 8.0.x патча (не до 9/10).

- [ ] **Step 2: Заменить ссылку в csproj**

В `src/Mailgunner/Mailgunner.csproj`: `<PackageReference Include="Polly" />` → `<PackageReference Include="Polly.Core" />`.

- [ ] **Step 3: Restore, сборка, аудит, тесты**

Run:
```bash
rtk dotnet restore Mailgunner.slnx
rtk dotnet list package --vulnerable --include-transitive
rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build
```
Expected: нет строки `has the following vulnerable packages`; 0 warnings; PASS.

- [ ] **Step 4: README, CHANGELOG, коммит**

README «Highlights»: `Polly` → `Polly.Core`. CHANGELOG `### Changed`: `- Runtime dependency floors lowered to the 8.0.x Extensions train (Microsoft.Extensions.Http 8.0.1, System.Text.Json 8.0.5); Polly replaced by the slimmer Polly.Core.`

```bash
rtk git add Directory.Packages.props src/Mailgunner/Mailgunner.csproj README.md CHANGELOG.md
rtk git commit -m "build: depend on Polly.Core and 8.0.x Extensions floors"
```

---

### Task 6: Общий HTTP-хелпер и текстовые guard-функции

`SendCoreAsync` скопирован в `MailgunSuppressionList`, `MailgunWebhooks` и (в варианте) в `MailgunnerClient`; `Add(content, name, value)` в трёх content-билдерах; `ContainsControlCharacter` в `EmailAddress` и `MailgunOptionsContent`. Сводим к одному месту. Поведение не меняется, поэтому новых тестов нет: гарантия это существующий набор.

**Files:**
- Create: `src/Mailgunner/Internal/MailgunHttp.cs`
- Create: `src/Mailgunner/Internal/TextGuards.cs`
- Modify: `src/Mailgunner/MailgunnerClient.cs:83-105`
- Modify: `src/Mailgunner/Internal/MailgunSuppressionList.cs` (удалить `SendCoreAsync`)
- Modify: `src/Mailgunner/Internal/MailgunWebhooks.cs` (удалить `SendCoreAsync`)
- Modify: `src/Mailgunner/Internal/MailgunMessageContent.cs`, `MailgunBatchContent.cs`, `MailgunOptionsContent.cs` (удалить локальный `Add`)
- Modify: `src/Mailgunner/EmailAddress.cs` (удалить локальный `ContainsControlCharacter`)

**Interfaces:**
- Produces:
  - `static Task<(int Status, string Body)> MailgunHttp.SendAsync(HttpClient httpClient, HttpRequestMessage request, CancellationToken cancellationToken)` — disposes request и response, бросает `MailgunnerException` на не-2xx.
  - `static void MailgunHttp.AddField(MultipartFormDataContent content, string name, string value)`.
  - `static bool TextGuards.ContainsControlCharacter(string value)`, `static bool TextGuards.ContainsLineBreak(string value)`.

- [ ] **Step 1: Создать `MailgunHttp.cs`**

```csharp
namespace Mailgunner.Internal;

/// <summary>
/// The single request/response primitive shared by sending, suppressions, and webhooks, so every
/// capability honors one error contract: any non-success response surfaces as
/// <see cref="MailgunnerException"/> carrying the status code and raw body.
/// </summary>
internal static class MailgunHttp
{
    /// <summary>
    /// Issues <paramref name="request"/>, reads the body, and throws <see cref="MailgunnerException"/> on a
    /// non-success status. Disposes the request and the response.
    /// </summary>
    /// <param name="httpClient">The configured typed client.</param>
    /// <param name="request">The request to send (disposed by this method).</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The status code and raw body of a success response.</returns>
    public static async System.Threading.Tasks.Task<(int Status, string Body)> SendAsync(
        System.Net.Http.HttpClient httpClient,
        System.Net.Http.HttpRequestMessage request,
        System.Threading.CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
#if NET8_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            if (!response.IsSuccessStatusCode)
            {
                throw new MailgunnerException((int)response.StatusCode, body);
            }

            return ((int)response.StatusCode, body);
        }
    }

    /// <summary>Appends one string field to a multipart body.</summary>
    /// <param name="content">The multipart body being built.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The field value.</param>
    public static void AddField(System.Net.Http.MultipartFormDataContent content, string name, string value) =>
        content.Add(new System.Net.Http.StringContent(value), name);
}
```

- [ ] **Step 2: Создать `TextGuards.cs`**

```csharp
namespace Mailgunner.Internal;

/// <summary>Character-class checks shared by the address, header, and option validators.</summary>
internal static class TextGuards
{
    /// <summary>Returns whether <paramref name="value"/> contains any Unicode control character (including CR/LF and TAB).</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when a control character is present.</returns>
    public static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="value"/> contains a carriage return or line feed.</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when a line break is present.</returns>
    public static bool ContainsLineBreak(string value) =>
        value.IndexOfAny(new[] { '\r', '\n' }) >= 0;
}
```

- [ ] **Step 3: Переключить вызывающих**

- `MailgunnerClient.SendContentAsync` заменить целиком:

```csharp
    private async System.Threading.Tasks.Task<SendResult> SendContentAsync(
        System.Net.Http.HttpContent content,
        System.Threading.CancellationToken cancellationToken)
    {
        var request = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post, new Uri($"v3/{_domain}/messages", UriKind.Relative))
        {
            Content = content,
        };

        var (status, body) = await MailgunHttp.SendAsync(HttpClient, request, cancellationToken).ConfigureAwait(false);

        if (TryParseResult(body, out var result))
        {
            return result;
        }

        throw new MailgunnerException(status, body);
    }
```

- В `MailgunSuppressionList` и `MailgunWebhooks` удалить приватный `SendCoreAsync`; все вызовы `SendCoreAsync(req, ct)` заменить на `MailgunHttp.SendAsync(_httpClient, req, ct)`.
- В `MailgunMessageContent`, `MailgunBatchContent`, `MailgunOptionsContent` удалить приватный `Add(...)`, вызовы `Add(content, ...)` заменить на `MailgunHttp.AddField(content, ...)`.
- В `MailgunOptionsContent` удалить приватные `ContainsLineBreak`/`ContainsControlCharacter`, вызовы заменить на `TextGuards.*`.
- В `EmailAddress` удалить приватный `ContainsControlCharacter`, вызовы заменить на `Internal.TextGuards.ContainsControlCharacter` (файл в namespace `Mailgunner`, поэтому нужен `using Mailgunner.Internal;` в начале файла).

- [ ] **Step 4: Сборка и полный прогон**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build`
Expected: 0 warnings, PASS. Проверить, что дубликатов не осталось:

```bash
rtk grep -rn "SendCoreAsync\|private static void Add(" src/Mailgunner
```
Expected: пусто.

- [ ] **Step 5: Коммит**

```bash
rtk git add src/Mailgunner
rtk git commit -m "refactor: share one HTTP send primitive and text guards across capabilities"
```

---

## Фаза 3. Безопасность

### Task 7: `EmailAddress` отвергает символы списка адресов

Адрес `"victim@x.com, attacker@y.com"` сейчас уходит одной частью `to`, и Mailgun разберёт его как двух получателей. Адреса часто приходят из пользовательских форм.

**Files:**
- Modify: `src/Mailgunner/EmailAddress.cs:19-40`
- Test: `tests/Mailgunner.Tests/EmailAddressTests.cs`

**Interfaces:**
- Consumes: `TextGuards.ContainsControlCharacter` (Task 6)
- Produces: конструктор `EmailAddress(string address, string? displayName = null)` дополнительно бросает `ArgumentException`, если `address` содержит `, ; < > " ( ) [ ] \` или пробельный символ, либо не содержит ровно один `@` не на краях.

- [ ] **Step 1: Написать падающие тесты**

В `EmailAddressTests.cs` добавить:

```csharp
    [Theory]
    [InlineData("victim@x.com, attacker@y.com")]
    [InlineData("victim@x.com; attacker@y.com")]
    [InlineData("victim@x.com <attacker@y.com>")]
    [InlineData("\"attacker\" victim@x.com")]
    [InlineData("victim@x.com (note)")]
    [InlineData("victim@[x.com]")]
    [InlineData("victim@x.com\\attacker")]
    [InlineData("victim @x.com")]
    [InlineData("victim")]
    [InlineData("@x.com")]
    [InlineData("victim@")]
    [InlineData("a@b@c.com")]
    public void Address_with_list_or_delimiter_characters_or_malformed_at_throws_ArgumentException(string address)
    {
        Assert.Throws<ArgumentException>(() => new EmailAddress(address));
    }

    [Theory]
    [InlineData("user+tag@example.com")]
    [InlineData("first.last@sub.example.co.uk")]
    [InlineData("o'brien@example.com")]
    [InlineData("юзер@пример.рф")]
    public void Ordinary_addresses_are_accepted(string address)
    {
        Assert.Equal(address, new EmailAddress(address).Address);
    }
```

- [ ] **Step 2: Убедиться что тесты падают**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "FullyQualifiedName~EmailAddressTests" -v q`
Expected: `Address_with_list_or_delimiter...` FAIL для большинства входов (исключение не брошено).

- [ ] **Step 3: Реализовать проверку**

В конструктор `EmailAddress` после проверки на управляющие символы добавить:

```csharp
        if (!IsPlainAddrSpec(address))
        {
            throw new System.ArgumentException(
                "An email address must be a bare addr-spec: exactly one '@' with a non-empty local part and domain, "
                + "and no whitespace, quotes, brackets, parentheses, backslashes, commas, or semicolons.",
                nameof(address));
        }
```

и приватный метод:

```csharp
    private static readonly char[] DelimiterCharacters = { ',', ';', '<', '>', '"', '(', ')', '[', ']', '\\' };

    /// <summary>
    /// Accepts only a bare <c>local@domain</c> form so a single value can never be parsed by the service
    /// as an address list, a display-name form, or a comment. Deliberately not a full RFC 5322 validator.
    /// </summary>
    private static bool IsPlainAddrSpec(string address)
    {
        if (address.IndexOfAny(DelimiterCharacters) >= 0)
        {
            return false;
        }

        foreach (var c in address)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        var at = address.IndexOf('@');
        return at > 0 && at < address.Length - 1 && address.IndexOf('@', at + 1) < 0;
    }
```

Обновить XML-док конструктора (`<exception>`): перечислить новые причины.

- [ ] **Step 4: Прогнать весь набор**

Run: `rtk dotnet test Mailgunner.slnx -c Release -v q`
Expected: PASS. Если какой-то тест использует адрес без `@` или с пробелом как валидный, заменить фикстуру на обычный адрес (`user@example.com`), не ослаблять проверку.

- [ ] **Step 5: CHANGELOG и коммит**

`### Security`: `- EmailAddress now rejects list/delimiter characters (, ; < > " ( ) [ ] \ and whitespace) and malformed '@' placement, so a single caller-supplied value can no longer smuggle extra recipients.`

```bash
rtk git add src/Mailgunner/EmailAddress.cs tests/Mailgunner.Tests/EmailAddressTests.cs CHANGELOG.md
rtk git commit -m "security: reject address-list delimiters in EmailAddress"
```

---

### Task 8: Экранирование домена в путях

**Files:**
- Modify: `src/Mailgunner/MailgunnerClient.cs:24`
- Test: `tests/Mailgunner.Tests/Registration/RegionRoutingTests.cs` (добавить тест)

- [ ] **Step 1: Написать падающий тест**

В `RegionRoutingTests.cs` добавить:

```csharp
    [Fact]
    public async Task Domain_is_percent_encoded_in_the_request_path()
    {
        var fake = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com/../other", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMailgunnerClient>();

        await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal("/v3/mg.example.com%2F..%2Fother/bounces", fake.LastRequest!.RequestUri!.AbsolutePath);
    }
```

(`CapturingHttpMessageHandler` возвращает 200 без тела; пустое тело десериализуется в `null`-страницу без исключения. Если вместо `MailgunnerException` метод вернёт пустую страницу, убрать `Assert.ThrowsAsync` и просто `await` вызов.)

- [ ] **Step 2: Убедиться что тест падает**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "Domain_is_percent_encoded" -v q`
Expected: FAIL, путь содержит `/../`.

- [ ] **Step 3: Экранировать один раз в конструкторе клиента**

В `MailgunnerClient` конструкторе: `_domain = Uri.EscapeDataString(options.Value.Domain.Trim());`. Обновить XML-док поля/параметра: «домен, обрезанный и percent-encoded для использования в пути». `MailgunSuppressions`, `MailgunWebhooks` и `ValidateCursor` продолжают получать уже экранированный домен, для обычных hostname он совпадает с исходным.

- [ ] **Step 4: Прогнать тесты и коммит**

Run: `rtk dotnet test Mailgunner.slnx -c Release -v q` → PASS.

```bash
rtk git add src/Mailgunner/MailgunnerClient.cs tests/Mailgunner.Tests/Registration/RegionRoutingTests.cs
rtk git commit -m "security: percent-encode the sending domain in request paths"
```

---

### Task 9: Безопасный режим ретраев для отправок (`SendRetryMode`)

Повтор `POST /messages` после таймаута или обрыва может доставить письмо дважды (у Mailgun нет idempotency-key). По умолчанию отправки ретраятся только на HTTP 429; suppressions и webhooks сохраняют полную политику. `SendRetryMode.Full` возвращает прежнее поведение.

**Files:**
- Create: `src/Mailgunner/SendRetryMode.cs`
- Create: `src/Mailgunner/Internal/MailgunRequestMarkers.cs`
- Modify: `src/Mailgunner/RetryPolicyOptions.cs`
- Modify: `src/Mailgunner/MailgunnerClient.cs` (пометить запрос отправки)
- Modify: `src/Mailgunner/Internal/MailgunResilienceHandler.cs`
- Modify: `tests/Mailgunner.Tests/Retry/RetryTestHarness.cs:43-49` (harness использует `Full`)
- Test: `tests/Mailgunner.Tests/Retry/SendRetryModeTests.cs` (создать)

**Interfaces:**
- Produces: `public enum SendRetryMode { Safe, Full }`; `RetryPolicyOptions.SendRetryMode` (default `Safe`); `MailgunRequestMarkers.MarkAsSend(HttpRequestMessage)`, `MailgunRequestMarkers.IsSend(HttpRequestMessage)`.

- [ ] **Step 1: Перевести существующий retry-harness на `Full`**

В `RetryTestHarness.BuildProvider` внутри делегата `AddMailgunner(options => {...})` перед `configure?.Invoke(options);` добавить строку:

```csharp
            options.Retry.SendRetryMode = SendRetryMode.Full; // existing retry tests exercise the full pipeline via sends
```

Так все существующие тесты в `tests/Mailgunner.Tests/Retry/` остаются тестами полной политики.

- [ ] **Step 2: Написать падающие тесты безопасного режима**

Создать `tests/Mailgunner.Tests/Retry/SendRetryModeTests.cs`:

```csharp
using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class SendRetryModeTests
{
    private const string Busy = "{\"message\":\"busy\"}";

    [Fact]
    public void Safe_is_the_default_mode()
    {
        Assert.Equal(SendRetryMode.Safe, new RetryPolicyOptions().SendRetryMode);
    }

    [Fact]
    public async Task Safe_mode_does_not_retry_a_send_on_503()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, Busy);
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Equal(503, ex.StatusCode);
        Assert.Single(stub.Requests);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task Safe_mode_does_not_retry_a_send_on_a_transport_failure()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            TransientFailureSelector = index => index == 0,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Safe_mode_still_retries_a_send_on_429()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.TooManyRequests, Busy) : null,
        };
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task Safe_mode_keeps_the_full_policy_for_non_send_requests()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"items\":[],\"paging\":{}}")
        {
            ResponseSelector = index => index == 0 ? (HttpStatusCode.ServiceUnavailable, Busy) : null,
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe);

        await client.Suppressions.Bounces.ListPageAsync();

        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Safe_mode_logs_no_exhaustion_record_for_an_unretried_send()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, Busy);
        var logger = new CapturingLoggerProvider();
        var client = RetryTestHarness.BuildClient(
            stub, new RecordingTimeProvider(), configure: o => o.Retry.SendRetryMode = SendRetryMode.Safe, loggerProvider: logger);

        await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.DoesNotContain(logger.Records, r => r.EventId == 1);
    }
}
```

- [ ] **Step 3: Убедиться что не компилируется**

Run: `rtk dotnet build tests/Mailgunner.Tests`
Expected: ошибка, `SendRetryMode` не найден.

- [ ] **Step 4: Добавить enum и опцию**

`src/Mailgunner/SendRetryMode.cs`:

```csharp
namespace Mailgunner;

/// <summary>
/// How automatic retry treats a message send (<c>POST /messages</c>), which is not idempotent: if the
/// service accepted the message but the response was lost, a retry delivers the email again. Requests
/// to the suppression and webhook endpoints are unaffected by this setting and always use the full policy.
/// </summary>
public enum SendRetryMode
{
    /// <summary>
    /// Retry a send only on HTTP <c>429</c> (rate limited, the message was not accepted). Timeouts,
    /// transport faults, <c>408</c>, and <c>5xx</c> surface after a single attempt. The default.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Retry a send under the same rules as every other request (<c>429</c>/<c>408</c>/<c>5xx</c> and
    /// transient transport faults). Accepts the risk of duplicate delivery in exchange for fewer surfaced failures.
    /// </summary>
    Full = 1,
}
```

В `RetryPolicyOptions` добавить:

```csharp
    /// <summary>
    /// Gets or sets how a message send is retried. Defaults to <see cref="Mailgunner.SendRetryMode.Safe"/>
    /// (retry only on <c>429</c>) because a send is not idempotent. See <see cref="Mailgunner.SendRetryMode"/>.
    /// </summary>
    public SendRetryMode SendRetryMode { get; set; } = SendRetryMode.Safe;
```

- [ ] **Step 5: Маркер запроса**

`src/Mailgunner/Internal/MailgunRequestMarkers.cs`:

```csharp
namespace Mailgunner.Internal;

/// <summary>
/// Tags a message-send request so the resilience handler can apply <see cref="SendRetryMode"/> to it and
/// the full policy to everything else. Uses <c>HttpRequestMessage.Options</c> on modern targets and the
/// legacy <c>Properties</c> bag on netstandard2.0.
/// </summary>
internal static class MailgunRequestMarkers
{
    private const string SendKeyName = "Mailgunner.IsSend";

#if NET8_0_OR_GREATER
    private static readonly System.Net.Http.HttpRequestOptionsKey<bool> SendKey = new(SendKeyName);

    public static void MarkAsSend(System.Net.Http.HttpRequestMessage request) => request.Options.Set(SendKey, true);

    public static bool IsSend(System.Net.Http.HttpRequestMessage request) =>
        request.Options.TryGetValue(SendKey, out var isSend) && isSend;
#else
    public static void MarkAsSend(System.Net.Http.HttpRequestMessage request) => request.Properties[SendKeyName] = true;

    public static bool IsSend(System.Net.Http.HttpRequestMessage request) =>
        request.Properties.TryGetValue(SendKeyName, out var value) && value is true;
#endif
}
```

В `MailgunnerClient.SendContentAsync` (Task 6) после создания `request` добавить `MailgunRequestMarkers.MarkAsSend(request);`.

- [ ] **Step 6: Учесть режим в хендлере**

В `MailgunResilienceHandler`:

1. Новый ключ рядом с `AttemptCounterKey`:
   ```csharp
   private static readonly ResiliencePropertyKey<bool> IsSendKey = new("Mailgunner.IsSend");
   ```
2. В `SendAsync` после `context.Properties.Set(AttemptCounterKey, counter);`:
   ```csharp
   context.Properties.Set(IsSendKey, MailgunRequestMarkers.IsSend(request));
   ```
3. Условие exhaustion-лога для ответа заменить на:
   ```csharp
   if (_options.MaxRetryAttempts > 0
       && counter.Retries >= _options.MaxRetryAttempts
       && RetryClassification.IsRetryableStatus((int)response.StatusCode))
   ```
4. `ShouldHandle = args => new ValueTask<bool>(ShouldRetry(args.Outcome, args.Context)),` и метод (перестаёт быть static):
   ```csharp
   private bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context)
   {
       var isSend = context.Properties.TryGetValue(IsSendKey, out var send) && send;
       if (isSend && _options.SendRetryMode == SendRetryMode.Safe)
       {
           // A send is not idempotent: only a rate-limit rejection is provably unaccepted.
           return outcome.Result is { } rejected && (int)rejected.StatusCode == 429;
       }

       if (outcome.Exception is { } exception)
       {
           return RetryClassification.IsTransientTransport(exception, context.CancellationToken);
       }

       return outcome.Result is { } response
           && RetryClassification.IsRetryableStatus((int)response.StatusCode);
   }
   ```

- [ ] **Step 7: Прогнать всё**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build`
Expected: 0 warnings, PASS (старые retry-тесты через `Full`, новые через `Safe`).

- [ ] **Step 8: README, CHANGELOG, коммит**

README, раздел «Automatic retry & backoff»: заменить первый список на формулировку с двумя правилами и добавить строку в блок настройки:

```
- **Sends are special** — `POST /messages` is not idempotent, so by default a send is retried **only on 429**
  (`Retry.SendRetryMode = SendRetryMode.Safe`). Set `SendRetryMode.Full` to retry sends on 408/5xx and transport
  faults too, accepting the risk of duplicate delivery. Suppression and webhook requests always use the full policy.
```
и `o.Retry.SendRetryMode = SendRetryMode.Safe;               // Safe (429 only) or Full`.

CHANGELOG `### Changed`: `- Message sends are retried only on HTTP 429 by default (new RetryPolicyOptions.SendRetryMode, default Safe); SendRetryMode.Full restores the previous behaviour. Non-send requests are unaffected.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: safe-by-default retry mode for non-idempotent message sends"
```

---

## Фаза 4. Устойчивость и ошибки

### Task 10: Таймаут на попытку вместо общего `HttpClient.Timeout`

Сейчас `HttpClient.Timeout` (100 с) накрывает весь retry-пайплайн. Вводим `RetryPolicyOptions.AttemptTimeout` (по умолчанию 100 с), реализованный в хендлере через linked CTS на системных часах (не через `TimeProvider`, чтобы `RecordingTimeProvider` в тестах не считал его задержкой), а `HttpClient.Timeout` ставим в `Infinite`.

**Files:**
- Modify: `src/Mailgunner/RetryPolicyOptions.cs`
- Modify: `src/Mailgunner/Internal/MailgunnerOptionsValidator.cs:46-60`
- Modify: `src/Mailgunner/Internal/MailgunResilienceHandler.cs:105-111`
- Modify: `src/Mailgunner/DependencyInjection/MailgunnerServiceCollectionExtensions.cs` (оба `ConfigureHttpClient`)
- Modify: `tests/Mailgunner.Tests/Fakes/StubHttpMessageHandler.cs` (хук `BeforeResponse`)
- Test: `tests/Mailgunner.Tests/Retry/AttemptTimeoutTests.cs` (создать), `tests/Mailgunner.Tests/Registration/ConfigurationValidationTests.cs`

**Interfaces:**
- Produces: `RetryPolicyOptions.AttemptTimeout` (`TimeSpan`, default 100 s, must be > 0); `StubHttpMessageHandler.BeforeResponse` (`Func<int, CancellationToken, Task>?`).

- [ ] **Step 1: Хук в стабе**

В `StubHttpMessageHandler` добавить свойство и вызов (после `OnSend?.Invoke(cancellationToken);`):

```csharp
    /// <summary>
    /// An optional per-request-index asynchronous hook awaited before the response is produced, with
    /// the request's cancellation token. Lets a test model a hanging attempt (await an infinite delay
    /// bound to the token) so a per-attempt timeout can be exercised offline.
    /// </summary>
    public Func<int, CancellationToken, Task>? BeforeResponse { get; set; }
```
```csharp
        if (BeforeResponse is not null)
        {
            await BeforeResponse(index, cancellationToken).ConfigureAwait(false);
        }
```

- [ ] **Step 2: Написать падающие тесты**

`tests/Mailgunner.Tests/Retry/AttemptTimeoutTests.cs`:

```csharp
using System.Net;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Retry;

public class AttemptTimeoutTests
{
    private static StubHttpMessageHandler HangingFirstAttempt() =>
        new(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            BeforeResponse = (index, ct) => index == 0 ? Task.Delay(Timeout.InfiniteTimeSpan, ct) : Task.CompletedTask,
        };

    [Fact]
    public async Task A_hanging_attempt_is_abandoned_after_the_attempt_timeout_and_retried_in_full_mode()
    {
        var stub = HangingFirstAttempt();
        var client = RetryTestHarness.BuildClient(
            stub, new RecordingTimeProvider(), configure: o => o.Retry.AttemptTimeout = TimeSpan.FromMilliseconds(50));

        var result = await client.SendAsync(RetryTestHarness.NewMessage());

        Assert.NotNull(result);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task A_hanging_send_surfaces_a_TimeoutException_in_safe_mode()
    {
        var stub = HangingFirstAttempt();
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider(), configure: o =>
        {
            o.Retry.AttemptTimeout = TimeSpan.FromMilliseconds(50);
            o.Retry.SendRetryMode = SendRetryMode.Safe;
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(RetryTestHarness.NewMessage()));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_during_an_attempt_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, RetryTestHarness.SuccessBody)
        {
            BeforeResponse = (_, ct) => { cts.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, ct); },
        };
        var client = RetryTestHarness.BuildClient(stub, new RecordingTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(RetryTestHarness.NewMessage(), cts.Token));

        Assert.Single(stub.Requests);
    }

    [Fact]
    public void The_typed_http_client_has_no_overall_timeout()
    {
        var client = (MailgunnerClient)RetryTestHarness.BuildClient(new StubHttpMessageHandler(HttpStatusCode.OK));

        Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
    }
}
```

В `ConfigurationValidationTests.cs` добавить:

```csharp
    [Fact]
    public void Non_positive_attempt_timeout_fails_at_startup()
    {
        var ex = ValidateThrows(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
            o.Region = MailgunRegion.Us;
            o.Retry.AttemptTimeout = TimeSpan.Zero;
        });

        Assert.Contains(ex.Failures, f => f.Contains("AttemptTimeout", StringComparison.Ordinal));
    }
```

- [ ] **Step 3: Убедиться что не компилируется**

Run: `rtk dotnet build tests/Mailgunner.Tests` → ошибка: `AttemptTimeout` не найден.

- [ ] **Step 4: Опция и валидация**

`RetryPolicyOptions`:

```csharp
    /// <summary>
    /// Gets or sets the maximum duration of a <em>single</em> attempt (connect, send, and read of the
    /// response). An attempt exceeding it is abandoned and surfaces as <see cref="System.TimeoutException"/>,
    /// which the retry policy treats as a transient transport fault. Replaces the typed client's overall
    /// <c>HttpClient.Timeout</c>, which the library sets to infinite so backoff waits are never cut short.
    /// Must be <c>&gt; <see cref="System.TimeSpan.Zero"/></c>. Defaults to 100&#160;seconds.
    /// </summary>
    public System.TimeSpan AttemptTimeout { get; set; } = System.TimeSpan.FromSeconds(100);
```

`MailgunnerOptionsValidator`, внутри `else` после проверки `MaxSingleWait`:

```csharp
            if (retry.AttemptTimeout <= System.TimeSpan.Zero)
            {
                failures.Add("The attempt timeout must be greater than zero (MailgunnerOptions.Retry.AttemptTimeout).");
            }
```

- [ ] **Step 5: Хендлер**

В `MailgunResilienceHandler.SendAsync` заменить вызов `_pipeline.ExecuteAsync(...)`:

```csharp
            var response = await _pipeline
                .ExecuteAsync(
                    async ctx => await SendAttemptAsync(request, ctx.CancellationToken).ConfigureAwait(false),
                    context)
                .ConfigureAwait(false);
```

и добавить метод:

```csharp
    /// <summary>
    /// Runs one attempt under <see cref="RetryPolicyOptions.AttemptTimeout"/>. A timeout is reported as
    /// <see cref="TimeoutException"/> (retryable under the full policy); the caller's own cancellation
    /// propagates unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> SendAttemptAsync(HttpRequestMessage request, CancellationToken callerToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        attempt.CancelAfter(_options.AttemptTimeout);
        try
        {
            return await base.SendAsync(request, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (attempt.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Mailgun request attempt exceeded the attempt timeout of {_options.AttemptTimeout}.", ex);
        }
    }
```

`TimeoutException` уже классифицируется как transient в `RetryClassification.IsTransientTransport`.

- [ ] **Step 6: Бесконечный `HttpClient.Timeout`**

В обоих `ConfigureHttpClient`-делегатах в `MailgunnerServiceCollectionExtensions` (unnamed строка ~75, named строка ~198) добавить первой строкой:

```csharp
            client.Timeout = Timeout.InfiniteTimeSpan; // per-attempt timeout lives in the resilience handler
```

- [ ] **Step 7: Прогнать всё**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build`
Expected: 0 warnings, PASS.

- [ ] **Step 8: README, CHANGELOG, коммит**

README, блок настройки retry: `o.Retry.AttemptTimeout = TimeSpan.FromSeconds(100);      // cap on a single attempt; HttpClient.Timeout is set to infinite`.
CHANGELOG `### Added`: `- RetryPolicyOptions.AttemptTimeout (default 100 s) bounds each attempt; the typed HttpClient's overall Timeout is now infinite so retries and backoff are never cut short by it.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: per-attempt timeout in the resilience handler"
```

---

### Task 11: Верхняя граница `MaxRetryAttempts`

При `MaxRetryAttempts` около 40 `Math.Pow(2, n) * ticks` переполняет `long` и Polly молча подставляет свою задержку.

**Files:**
- Modify: `src/Mailgunner/Internal/MailgunnerOptionsValidator.cs`
- Modify: `src/Mailgunner/RetryPolicyOptions.cs` (док)
- Modify: `src/Mailgunner/Internal/MailgunResilienceHandler.cs:194-203` (безопасное умножение)
- Test: `tests/Mailgunner.Tests/Registration/ConfigurationValidationTests.cs`, `tests/Mailgunner.Tests/Retry/BackoffIncreasesWithJitterTests.cs`

- [ ] **Step 1: Тесты**

В `ConfigurationValidationTests.cs`:

```csharp
    [Fact]
    public void More_than_ten_retry_attempts_fails_at_startup()
    {
        var ex = ValidateThrows(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
            o.Region = MailgunRegion.Us;
            o.Retry.MaxRetryAttempts = 11;
        });

        Assert.Contains(ex.Failures, f => f.Contains("MaxRetryAttempts", StringComparison.Ordinal));
    }
```

В `BackoffIncreasesWithJitterTests.cs` добавить тест, что при большой базе задержка упирается в cap, а не переполняется:

```csharp
    [Fact]
    public async Task A_large_base_delay_saturates_at_the_cap_instead_of_overflowing()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}");
        var time = new RecordingTimeProvider();
        var client = RetryTestHarness.BuildClient(stub, time, configure: o =>
        {
            o.Retry.MaxRetryAttempts = 10;
            o.Retry.BaseDelay = TimeSpan.FromDays(5000);
            o.Retry.MaxSingleWait = TimeSpan.FromDays(10000);
            o.Retry.UseJitter = true;
        });

        await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.ListPageAsync());

        Assert.Equal(10, time.Delays.Count);
        Assert.All(time.Delays, d => Assert.Equal(TimeSpan.FromDays(10000), d));
    }
```

(При 2^9 × 5000 дней тики уходят за `long.MaxValue`; без защиты задержка стала бы отрицательной.)

- [ ] **Step 2: Убедиться что падают**

Run: `rtk dotnet test tests/Mailgunner.Tests --filter "More_than_ten_retry_attempts_fails_at_startup|A_large_base_delay_saturates" -v q` → FAIL.

- [ ] **Step 3: Реализация**

Валидатор, рядом с проверкой `MaxRetryAttempts < 0`:

```csharp
            if (retry.MaxRetryAttempts > RetryPolicyOptions.MaxAllowedRetryAttempts)
            {
                failures.Add($"The maximum retry attempts must not exceed {RetryPolicyOptions.MaxAllowedRetryAttempts} (MailgunnerOptions.Retry.MaxRetryAttempts).");
            }
```

`RetryPolicyOptions`: константа и правка дока `MaxRetryAttempts` («Must be between 0 and 10»):

```csharp
    /// <summary>The largest accepted <see cref="MaxRetryAttempts"/>; bounds the exponential schedule.</summary>
    public const int MaxAllowedRetryAttempts = 10;
```

`MailgunResilienceHandler.ComputeDelay`: считать в `double` и насыщать до cap до преобразования в `TimeSpan`:

```csharp
        var capTicks = (double)_options.MaxSingleWait.Ticks;
        var baseTicks = Math.Min(_options.BaseDelay.Ticks * Math.Pow(2, attemptNumber), capTicks);
        var jitterTicks = _options.UseJitter ? baseTicks * _random.NextDouble() * JitterFraction : 0d;
        var totalTicks = Math.Min(baseTicks + jitterTicks, capTicks);
        return TimeSpan.FromTicks((long)totalTicks);
```

Метод `Multiply` удалить.

- [ ] **Step 4: Прогнать всё, CHANGELOG, коммит**

Run: полный build+test → PASS.
CHANGELOG `### Changed`: `- MaxRetryAttempts is validated to be at most 10; backoff math saturates at MaxSingleWait instead of overflowing.`

```bash
rtk git add src tests CHANGELOG.md
rtk git commit -m "fix: bound MaxRetryAttempts and saturate backoff at the cap"
```

---

### Task 12: `MailgunnerException` с текстом ошибки Mailgun и частичными результатами батча

**Files:**
- Modify: `src/Mailgunner/MailgunnerException.cs`
- Modify: `src/Mailgunner/MailgunnerClient.cs` (`SendBatchAsync`)
- Test: `tests/Mailgunner.Tests/Sending/SendErrorTests.cs`, `tests/Mailgunner.Tests/Sending/BatchFailureTests.cs`

**Interfaces:**
- Produces: `MailgunnerException(int statusCode, string responseBody, int? failedChunkIndex, IReadOnlyList<SendResult> acceptedResults)`; свойства `int? FailedChunkIndex`, `IReadOnlyList<SendResult> AcceptedResults` (пустой список по умолчанию). `Message` включает `message` из JSON-тела (до 200 символов).

- [ ] **Step 1: Тесты**

В `SendErrorTests.cs` добавить:

```csharp
    [Fact]
    public async Task Exception_message_includes_the_service_message_from_a_json_body()
    {
        var (client, _) = BuildClient(HttpStatusCode.BadRequest, "{\"message\":\"'from' parameter is missing\"}");

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal("The Mailgun request failed (HTTP 400): 'from' parameter is missing", ex.Message);
    }

    [Fact]
    public async Task Exception_message_stays_generic_for_a_non_json_body()
    {
        var (client, _) = BuildClient(HttpStatusCode.BadGateway, "<html>502</html>");

        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(NewMessage()));

        Assert.Equal("The Mailgun request did not yield a usable result (HTTP 502).", ex.Message);
        Assert.Null(ex.FailedChunkIndex);
        Assert.Empty(ex.AcceptedResults);
    }

    [Fact]
    public void A_long_service_message_is_truncated_to_200_characters()
    {
        var body = "{\"message\":\"" + new string('x', 500) + "\"}";

        var ex = new MailgunnerException(400, body);

        Assert.Equal("The Mailgun request failed (HTTP 400): " + new string('x', 200) + "…", ex.Message);
    }
```

(Если `SendErrorTests` не имеет `BuildClient`/`NewMessage`, скопировать их из `SendMessageTests.cs`.)

В `BatchFailureTests.cs` в тест `Non_success_on_second_chunk_fails_fast...` добавить проверки:

```csharp
        Assert.Equal(1, ex.FailedChunkIndex);
        Assert.Single(ex.AcceptedResults); // chunk 0 was accepted before chunk 1 failed
        Assert.Equal("<x@mg>", ex.AcceptedResults[0].Id);
```

- [ ] **Step 2: Убедиться что не компилируется** — `FailedChunkIndex` не найден.

- [ ] **Step 3: Реализация исключения**

Заменить тело класса `MailgunnerException`:

```csharp
    private const int MaxServiceMessageLength = 200;

    /// <summary>Initializes a new instance for a single failed request.</summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="responseBody">The raw response body (never null; empty when the response had no body).</param>
    public MailgunnerException(int statusCode, string responseBody)
        : this(statusCode, responseBody, null, System.Array.Empty<SendResult>())
    {
    }

    /// <summary>Initializes a new instance for a batch chunk that failed after earlier chunks were accepted.</summary>
    /// <param name="statusCode">The HTTP status code of the failing chunk's response.</param>
    /// <param name="responseBody">The raw response body of the failing chunk.</param>
    /// <param name="failedChunkIndex">The zero-based index of the chunk that failed, or <see langword="null"/> outside a batch.</param>
    /// <param name="acceptedResults">The results of the chunks accepted before the failure, in order; empty outside a batch.</param>
    public MailgunnerException(
        int statusCode,
        string responseBody,
        int? failedChunkIndex,
        System.Collections.Generic.IReadOnlyList<SendResult> acceptedResults)
        : base(BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        FailedChunkIndex = failedChunkIndex;
        AcceptedResults = acceptedResults ?? System.Array.Empty<SendResult>();
    }

    /// <summary>Gets the HTTP status code of the response.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the raw response body. Never null; empty when the response had no body.</summary>
    public string ResponseBody { get; }

    /// <summary>
    /// Gets the zero-based index of the batch chunk that failed, or <see langword="null"/> when the error
    /// did not occur inside <see cref="IMailgunnerClient.SendBatchAsync"/>.
    /// </summary>
    public int? FailedChunkIndex { get; }

    /// <summary>
    /// Gets the results of the batch chunks Mailgun accepted before the failure (chunks
    /// <c>0..FailedChunkIndex-1</c>), so a caller can resume from the failed chunk. Empty outside a batch.
    /// Those messages have been sent and are not rolled back.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<SendResult> AcceptedResults { get; }

    private static string BuildMessage(int statusCode, string responseBody)
    {
        var serviceMessage = TryExtractServiceMessage(responseBody);
        return serviceMessage is null
            ? $"The Mailgun request did not yield a usable result (HTTP {statusCode})."
            : $"The Mailgun request failed (HTTP {statusCode}): {serviceMessage}";
    }

    /// <summary>Reads the <c>message</c> string of a JSON object body; null for anything else.</summary>
    private static string? TryExtractServiceMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !document.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            var text = message.GetString()!;
            return text.Length <= MaxServiceMessageLength ? text : text.Substring(0, MaxServiceMessageLength) + "…";
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
```

- [ ] **Step 4: Батч**

В `MailgunnerClient.SendBatchAsync` цикл заменить:

```csharp
        var chunkIndex = 0;
        foreach (var chunk in MailgunBatchContent.Chunk(message.Recipients, MailgunBatchContent.MaxRecipientsPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var content = MailgunBatchContent.BuildChunk(message, chunk);
            try
            {
                results.Add(await SendContentAsync(content, cancellationToken).ConfigureAwait(false));
            }
            catch (MailgunnerException ex)
            {
                throw new MailgunnerException(ex.StatusCode, ex.ResponseBody, chunkIndex, results.AsReadOnly());
            }

            chunkIndex++;
        }
```

В XML-доке `IMailgunnerClient.SendBatchAsync` (`<exception cref="MailgunnerException">`) дописать: «`FailedChunkIndex` и `AcceptedResults` показывают, какие чанки уже приняты».

- [ ] **Step 5: Прогнать всё, CHANGELOG, коммит**

Run: полный build+test → PASS. Проверить, что `NamedSecretHygieneTests`/`BatchFailureTests` (ключ не в `Message`) проходят: тело ответа теперь частично в `Message`, но ключ в тело ответа не попадает.

CHANGELOG `### Changed`: `- MailgunnerException.Message now includes Mailgun's "message" from a JSON error body (truncated to 200 chars).` и `### Added`: `- MailgunnerException.FailedChunkIndex / AcceptedResults expose which batch chunks were accepted before a failure.`

```bash
rtk git add src tests CHANGELOG.md
rtk git commit -m "feat: surface Mailgun's error message and batch partial results in MailgunnerException"
```

---

## Фаза 5. DX и покрытие API

### Task 13: `ReplyTo` первым классом и заменяемые `Options`

**Files:**
- Modify: `src/Mailgunner/MailgunMessage.cs`, `src/Mailgunner/MailgunBatchMessage.cs`
- Modify: `src/Mailgunner/Internal/MailgunOptionsContent.cs` (`Append` получает `replyTo`)
- Modify: `src/Mailgunner/Internal/MailgunMessageContent.cs:77`, `src/Mailgunner/Internal/MailgunBatchContent.cs:129`
- Test: `tests/Mailgunner.Tests/Sending/ReplyToTests.cs` (создать)

**Interfaces:**
- Produces: `EmailAddress? MailgunMessage.ReplyTo { get; set; }`, `EmailAddress? MailgunBatchMessage.ReplyTo { get; set; }`; `MailgunSendOptions Options { get; set; }` на обоих (не-null обязателен); `MailgunOptionsContent.Append(content, options, attachments, inlineFiles, EmailAddress? replyTo)`.

- [ ] **Step 1: Тесты**

`tests/Mailgunner.Tests/Sending/ReplyToTests.cs`:

```csharp
using System.Net;
using Mailgunner.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.Tests.Sending;

public class ReplyToTests
{
    private const string SuccessBody = "{\"id\":\"<x@mg>\",\"message\":\"Queued.\"}";

    private static (IMailgunnerClient Client, StubHttpMessageHandler Stub) BuildClient()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var services = new ServiceCollection();
        services.AddMailgunner("mg.example.com", "key-123", MailgunRegion.Us)
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        return (services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>(), stub);
    }

    private static MailgunMessage NewMessage()
    {
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");
        return message;
    }

    [Fact]
    public async Task ReplyTo_is_emitted_as_the_Reply_To_header()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.ReplyTo = new EmailAddress("support@example.com", "Support");

        await client.SendAsync(message);

        Assert.Equal("Support <support@example.com>", stub.LastFormData.Single(f => f.Name == "h:Reply-To").Value);
    }

    [Fact]
    public async Task ReplyTo_is_omitted_when_unset()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        Assert.DoesNotContain(stub.LastFormData, f => f.Name == "h:Reply-To");
    }

    [Fact]
    public async Task ReplyTo_conflicting_with_a_manual_header_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.ReplyTo = "support@example.com";
        message.Options.CustomHeaders["reply-to"] = "other@example.com";

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Batch_ReplyTo_is_repeated_on_every_chunk()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t", ReplyTo = "support@example.com" };
        for (var i = 0; i < 1001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"u{i}@example.com"));
        }

        await client.SendBatchAsync(batch);

        Assert.All(stub.Requests, r => Assert.Equal("support@example.com", r.Value("h:Reply-To")));
    }

    [Fact]
    public async Task Options_can_be_replaced_with_a_shared_instance()
    {
        var (client, stub) = BuildClient();
        var shared = new MailgunSendOptions { TestMode = true };
        var message = NewMessage();
        message.Options = shared;

        await client.SendAsync(message);

        Assert.Equal("yes", stub.LastFormData.Single(f => f.Name == "o:testmode").Value);
    }

    [Fact]
    public async Task Null_options_throw_before_any_request()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options = null!;

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(message));

        Assert.Empty(stub.Requests);
    }
}
```

- [ ] **Step 2: Убедиться что не компилируется** (`ReplyTo`, setter `Options`).

- [ ] **Step 3: Модели**

В `MailgunMessage` и `MailgunBatchMessage`:

```csharp
    /// <summary>
    /// Gets or sets the optional reply-to address, emitted as the <c>Reply-To</c> header
    /// (<c>h:Reply-To</c>). Setting it and also supplying a <c>Reply-To</c> entry in
    /// <see cref="MailgunSendOptions.CustomHeaders"/> (matched case-insensitively) throws
    /// <see cref="System.ArgumentException"/> when the request is built.
    /// </summary>
    public EmailAddress? ReplyTo { get; set; }
```

`Options` на обоих: `public MailgunSendOptions Options { get; set; } = new MailgunSendOptions();` с доком «Never set to null; a null value is rejected when the request is built».

- [ ] **Step 4: Билдер**

`MailgunOptionsContent.Append` получает пятый параметр `EmailAddress? replyTo` и первой строкой:

```csharp
        if (options is null)
        {
            throw new System.ArgumentException("Message options must not be null.", nameof(options));
        }
```

После блока 7b (List-Unsubscribe) добавить блок:

```csharp
        // 7c. Reply-To — typed; conflicts with a manual header of the same name.
        if (replyTo is EmailAddress reply && !string.IsNullOrWhiteSpace(reply.Address))
        {
            foreach (var key in options.CustomHeaders.Keys)
            {
                if (string.Equals(key, "Reply-To", System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new System.ArgumentException(
                        "Reply-To is set both via ReplyTo and a manual CustomHeaders entry; use only one.", nameof(options));
                }
            }

            MailgunHttp.AddField(content, "h:Reply-To", reply.ToString());
        }
```

В `MailgunMessageContent.Build` и `MailgunBatchContent.BuildChunk` вызов: `MailgunOptionsContent.Append(content, message.Options, message.Attachments, message.InlineFiles, message.ReplyTo);`.

- [ ] **Step 5: Прогнать всё, README, CHANGELOG, коммит**

README, «Send options & limits»: добавить пункт `- **Reply-To** — \`message.ReplyTo = "support@example.com"\` emits the \`Reply-To\` header.`
CHANGELOG `### Added`: `- ReplyTo on MailgunMessage and MailgunBatchMessage (emitted as h:Reply-To); Options is now settable so one MailgunSendOptions can be shared.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: typed ReplyTo and replaceable send options"
```

---

### Task 14: Дополнительные опции отправки Mailgun

`o:require-tls`, `o:skip-verification`, `o:tracking`, `o:sending-ip`, `o:sending-ip-pool`, `o:time-zone-localize`, и поле сообщения `amp-html`.

**Files:**
- Modify: `src/Mailgunner/MailgunSendOptions.cs`
- Modify: `src/Mailgunner/MailgunMessage.cs` (`AmpHtml`)
- Modify: `src/Mailgunner/Internal/MailgunOptionsContent.cs`, `src/Mailgunner/Internal/MailgunMessageContent.cs`
- Test: `tests/Mailgunner.Tests/Sending/ExtendedSendOptionsTests.cs` (создать)

**Interfaces:**
- Produces на `MailgunSendOptions`: `bool? RequireTls`, `bool? SkipVerification`, `bool? Tracking`, `string? SendingIp`, `string? SendingIpPool`, `string? TimeZoneLocalize`. На `MailgunMessage`: `string? AmpHtml`.

- [ ] **Step 1: Тесты**

`tests/Mailgunner.Tests/Sending/ExtendedSendOptionsTests.cs` (с тем же `BuildClient`/`NewMessage`, что в `ReplyToTests`):

```csharp
    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public async Task Boolean_options_are_emitted_as_yes_or_no(bool value, string wire)
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.RequireTls = value;
        message.Options.SkipVerification = value;
        message.Options.Tracking = value;

        await client.SendAsync(message);

        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:require-tls").Value);
        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:skip-verification").Value);
        Assert.Equal(wire, stub.LastFormData.Single(f => f.Name == "o:tracking").Value);
    }

    [Fact]
    public async Task String_options_are_emitted_verbatim()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.Options.SendingIp = "192.0.2.10";
        message.Options.SendingIpPool = "pool-a";
        message.Options.TimeZoneLocalize = "09:00";

        await client.SendAsync(message);

        Assert.Equal("192.0.2.10", stub.LastFormData.Single(f => f.Name == "o:sending-ip").Value);
        Assert.Equal("pool-a", stub.LastFormData.Single(f => f.Name == "o:sending-ip-pool").Value);
        Assert.Equal("09:00", stub.LastFormData.Single(f => f.Name == "o:time-zone-localize").Value);
    }

    [Fact]
    public async Task Unset_options_are_omitted()
    {
        var (client, stub) = BuildClient();

        await client.SendAsync(NewMessage());

        foreach (var name in new[] { "o:require-tls", "o:skip-verification", "o:tracking", "o:sending-ip", "o:sending-ip-pool", "o:time-zone-localize", "amp-html" })
        {
            Assert.DoesNotContain(stub.LastFormData, f => f.Name == name);
        }
    }

    [Fact]
    public async Task Amp_html_is_emitted_as_its_own_part()
    {
        var (client, stub) = BuildClient();
        var message = NewMessage();
        message.AmpHtml = "<!doctype html><html ⚡4email></html>";

        await client.SendAsync(message);

        Assert.Equal(message.AmpHtml, stub.LastFormData.Single(f => f.Name == "amp-html").Value);
    }
```

- [ ] **Step 2: Убедиться что не компилируется.**

- [ ] **Step 3: Опции**

В `MailgunSendOptions` добавить (каждое с XML-доком по образцу `TrackingOpens`, указывая имя поля на wire):

```csharp
    /// <summary>Gets or sets whether TLS is required for delivery (<c>o:require-tls</c>); null omits the field.</summary>
    public bool? RequireTls { get; set; }

    /// <summary>Gets or sets whether certificate/hostname verification is skipped (<c>o:skip-verification</c>); null omits the field.</summary>
    public bool? SkipVerification { get; set; }

    /// <summary>Gets or sets the master tracking toggle (<c>o:tracking</c>) covering opens and clicks; null omits the field.</summary>
    public bool? Tracking { get; set; }

    /// <summary>Gets or sets the dedicated sending IP to use (<c>o:sending-ip</c>); null/blank omits the field.</summary>
    public string? SendingIp { get; set; }

    /// <summary>Gets or sets the IP pool to send from (<c>o:sending-ip-pool</c>); null/blank omits the field.</summary>
    public string? SendingIpPool { get; set; }

    /// <summary>
    /// Gets or sets the recipient-local delivery time (<c>o:time-zone-localize</c>, e.g. <c>"09:00"</c> or
    /// <c>"9:00AM"</c>) applied on top of <see cref="DeliveryTime"/>; null/blank omits the field.
    /// </summary>
    public string? TimeZoneLocalize { get; set; }
```

В `MailgunMessage`:

```csharp
    /// <summary>Gets or sets the optional AMP-HTML body part, emitted as <c>amp-html</c>. Requires <see cref="Html"/> or <see cref="Text"/> as well.</summary>
    public string? AmpHtml { get; set; }
```

- [ ] **Step 4: Эмиссия**

В `MailgunOptionsContent.Append` после блока 4 (click tracking) добавить:

```csharp
        // 4b. Additional o: toggles and strings — omitted when null/blank.
        AddYesNo(content, "o:require-tls", options.RequireTls);
        AddYesNo(content, "o:skip-verification", options.SkipVerification);
        AddYesNo(content, "o:tracking", options.Tracking);
        AddIfPresent(content, "o:sending-ip", options.SendingIp);
        AddIfPresent(content, "o:sending-ip-pool", options.SendingIpPool);
        AddIfPresent(content, "o:time-zone-localize", options.TimeZoneLocalize);
```

и два хелпера:

```csharp
    private static void AddYesNo(System.Net.Http.MultipartFormDataContent content, string name, bool? value)
    {
        if (value is bool flag)
        {
            MailgunHttp.AddField(content, name, flag ? "yes" : "no");
        }
    }

    private static void AddIfPresent(System.Net.Http.MultipartFormDataContent content, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            MailgunHttp.AddField(content, name, value!);
        }
    }
```

В `MailgunMessageContent.Build` после блока `html`:

```csharp
        if (!string.IsNullOrEmpty(message.AmpHtml))
        {
            MailgunHttp.AddField(content, "amp-html", message.AmpHtml!);
        }
```

- [ ] **Step 5: Прогнать всё, README, CHANGELOG, коммит**

README «Send options & limits»: пункт `- **Delivery controls** — \`RequireTls\`, \`SkipVerification\`, \`Tracking\` (master toggle), \`SendingIp\`, \`SendingIpPool\`, \`TimeZoneLocalize\`; \`MailgunMessage.AmpHtml\` for an AMP part.`
CHANGELOG `### Added`: `- Send options RequireTls, SkipVerification, Tracking, SendingIp, SendingIpPool, TimeZoneLocalize and MailgunMessage.AmpHtml.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: additional Mailgun send options and AMP-HTML part"
```

---

### Task 15: Батч с inline-телом (без stored template)

Канонический батч Mailgun это `%recipient.var%` в `text`/`html`/`subject` плюс `recipient-variables`. Разрешаем `Text`/`Html` на `MailgunBatchMessage`; правила те же, что у `MailgunMessage` (template XOR inline body).

**Files:**
- Modify: `src/Mailgunner/MailgunBatchMessage.cs`
- Modify: `src/Mailgunner/Internal/MailgunBatchContent.cs` (`Validate`, `BuildChunk`)
- Modify: `tests/Mailgunner.Tests/Sending/BatchValidationTests.cs:45` (тест `Missing_template_...`)
- Test: `tests/Mailgunner.Tests/Sending/BatchInlineBodyTests.cs` (создать)

**Interfaces:**
- Produces: `string? MailgunBatchMessage.Text`, `string? MailgunBatchMessage.Html`.

- [ ] **Step 1: Тесты**

Переименовать тест в `BatchValidationTests.cs:45` в `Missing_template_and_body_throws_argument_exception_and_issues_no_request` (тело без изменений: батч без `Template` и без `Text`/`Html` по-прежнему бросает).

`tests/Mailgunner.Tests/Sending/BatchInlineBodyTests.cs` (тот же `BuildClient`, что в `ReplyToTests`):

```csharp
    [Fact]
    public async Task Inline_text_and_html_batch_emits_body_parts_and_recipient_variables_without_a_template()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage
        {
            From = "noreply@mg.example.com",
            Subject = "Hi %recipient.name%",
            Text = "Hello %recipient.name%",
            Html = "<p>Hello %recipient.name%</p>",
        };
        var ada = new BatchRecipient("ada@example.com");
        ada.Variables["name"] = "Ada";
        batch.Recipients.Add(ada);

        await client.SendBatchAsync(batch);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("Hello %recipient.name%", request.Value("text"));
        Assert.Equal("<p>Hello %recipient.name%</p>", request.Value("html"));
        Assert.Equal("{\"ada@example.com\":{\"name\":\"Ada\"}}", request.Value("recipient-variables"));
        Assert.Null(request.Value("template"));
    }

    [Fact]
    public async Task Template_and_inline_body_together_throw_before_any_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t", Text = "x" };
        batch.Recipients.Add(new BatchRecipient("a@example.com"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Template_data_without_a_template_throws_before_any_request()
    {
        var (client, stub) = BuildClient();
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Text = "x", GenerateTextFromTemplate = true };
        batch.Recipients.Add(new BatchRecipient("a@example.com"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendBatchAsync(batch));

        Assert.Empty(stub.Requests);
    }
```

- [ ] **Step 2: Убедиться что не компилируется.**

- [ ] **Step 3: Модель**

В `MailgunBatchMessage` после `Subject`:

```csharp
    /// <summary>
    /// Gets or sets the plain-text body for an inline (non-template) batch. Use <c>%recipient.var%</c>
    /// placeholders that Mailgun fills from each recipient's <see cref="BatchRecipient.Variables"/>.
    /// Mutually exclusive with <see cref="Template"/>.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the HTML body for an inline (non-template) batch; see <see cref="Text"/>.</summary>
    public string? Html { get; set; }
```

Док `Template`: «Required unless Text or Html is set».

- [ ] **Step 4: Валидация и билдер**

В `MailgunBatchContent.Validate` заменить проверку `Template`:

```csharp
        var hasBody = !string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Html);
        var hasTemplate = !string.IsNullOrWhiteSpace(message.Template);

        if (!hasBody && !hasTemplate)
        {
            throw new System.ArgumentException(
                "A batch send requires a Template name or an inline body (Text or Html).", nameof(message));
        }

        if (hasBody && hasTemplate)
        {
            throw new System.ArgumentException(
                "A batch cannot have both a Template and an inline body (Text or Html); supply one or the other.",
                nameof(message));
        }

        var hasTemplateData = message.TemplateVariables.Count > 0
            || !string.IsNullOrWhiteSpace(message.TemplateVersion)
            || message.GenerateTextFromTemplate;

        if (hasTemplateData && !hasTemplate)
        {
            throw new System.ArgumentException(
                "Template variables, a template version, or a generated-text request require a Template name.",
                nameof(message));
        }
```

В `BuildChunk` заменить безусловный блок `template`/`t:*` на:

```csharp
        if (!string.IsNullOrEmpty(message.Text))
        {
            MailgunHttp.AddField(content, "text", message.Text!);
        }

        if (!string.IsNullOrEmpty(message.Html))
        {
            MailgunHttp.AddField(content, "html", message.Html!);
        }

        if (!string.IsNullOrWhiteSpace(message.Template))
        {
            MailgunHttp.AddField(content, "template", message.Template!);

            if (!string.IsNullOrWhiteSpace(message.TemplateVersion))
            {
                MailgunHttp.AddField(content, "t:version", message.TemplateVersion!);
            }

            if (message.GenerateTextFromTemplate)
            {
                MailgunHttp.AddField(content, "t:text", "yes");
            }

            if (message.TemplateVariables.Count > 0)
            {
                MailgunHttp.AddField(content, "t:variables", System.Text.Json.JsonSerializer.Serialize(message.TemplateVariables));
            }
        }
```

- [ ] **Step 5: Прогрнать всё, README, CHANGELOG, коммит**

README: в абзаце «Why the bridge (step 3)?» заменить «The library's batch send is stored-template-only» на «A batch can use a stored template (this example) **or** inline `Text`/`Html` with `%recipient.var%` placeholders».
CHANGELOG `### Added`: `- Batch sends without a stored template: MailgunBatchMessage.Text / Html with %recipient.var% placeholders.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: inline text/html batch sends with recipient variables"
```

---

### Task 16: Событие вебхука `accepted`

**Files:**
- Modify: `src/Mailgunner/WebhookEventType.cs`
- Modify: `src/Mailgunner/Internal/WebhookWireDtos.cs` (`WebhookEventTypes`)
- Modify: `tests/Mailgunner.Tests/WebhookManagement/WebhookEventTypeMappingTests.cs`

- [ ] **Step 1: Тесты**

В `WebhookEventTypeMappingTests.cs`: добавить `[InlineData(WebhookEventType.Accepted, "accepted")]` к round-trip-тесту и **удалить** строку `[InlineData("accepted")]` из `Unknown_tokens_parse_to_null`.

- [ ] **Step 2: Убедиться что не компилируется.**

- [ ] **Step 3: Реализация**

В enum добавить **последним** членом (чтобы не сдвигать существующие значения):

```csharp
    /// <summary>Mailgun accepted the message for delivery (<c>accepted</c>).</summary>
    Accepted,
```

Убрать из дока enum фразу про намеренное исключение `accepted`. В `WebhookEventTypes.ToToken` добавить `WebhookEventType.Accepted => "accepted",`, в `TryParseToken` `"accepted" => WebhookEventType.Accepted,`.

- [ ] **Step 4: Прогнать всё, CHANGELOG, коммит**

CHANGELOG `### Added`: `- WebhookEventType.Accepted.`

```bash
rtk git add src tests CHANGELOG.md
rtk git commit -m "feat: support the accepted webhook event type"
```

---

### Task 17: `MailgunFile` из потока

Большие вложения не должны жить в памяти как `byte[]`. Фабрика `Func<Stream>` открывает поток на каждую сериализацию, поэтому ретраи и повторные чанки работают.

**Files:**
- Modify: `src/Mailgunner/MailgunFile.cs`
- Create: `src/Mailgunner/Internal/StreamFactoryContent.cs`
- Modify: `src/Mailgunner/Internal/MailgunOptionsContent.cs` (`AddFile`)
- Test: `tests/Mailgunner.Tests/Sending/AttachmentTests.cs`

**Interfaces:**
- Produces: `MailgunFile(string fileName, Func<Stream> openContent, string? contentType = null, long? length = null)`; свойства `byte[]? Content` (nullable теперь), `Func<Stream>? OpenContent`, `long? Length`.

- [ ] **Step 1: Тесты**

В `AttachmentTests.cs` добавить:

```csharp
    [Fact]
    public async Task Stream_backed_attachment_is_read_from_a_fresh_stream_per_request()
    {
        var (client, stub) = BuildClient();
        var opened = 0;
        var batch = new MailgunBatchMessage { From = "noreply@mg.example.com", Template = "t" };
        for (var i = 0; i < 1001; i++)
        {
            batch.Recipients.Add(new BatchRecipient($"u{i}@example.com"));
        }

        batch.Attachments.Add(new MailgunFile(
            "report.txt",
            () => { opened++; return new MemoryStream(Encoding.UTF8.GetBytes("hello")); },
            "text/plain"));

        await client.SendBatchAsync(batch);

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(2, opened);
        Assert.All(stub.Requests, r => Assert.Equal("hello", r.Fields("attachment").Single().Value));
    }

    [Fact]
    public void Stream_file_requires_a_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new MailgunFile("a.txt", (Func<Stream>)null!));
    }
```

(`BuildClient` — как в других тестах файла; добавить `using System.Text;` при необходимости.)

- [ ] **Step 2: Убедиться что не компилируется.**

- [ ] **Step 3: Модель**

`MailgunFile`: сделать `Content` типом `byte[]?`, добавить второй конструктор и свойства:

```csharp
    /// <summary>
    /// Initializes a stream-backed file. <paramref name="openContent"/> is invoked once per request that
    /// carries the file (each batch chunk and each retry), so it must return a fresh readable stream every
    /// time; the library disposes each stream after copying it.
    /// </summary>
    /// <param name="fileName">The file name carried on the file part. Required, non-blank.</param>
    /// <param name="openContent">Opens a fresh stream over the content. Required.</param>
    /// <param name="contentType">The optional MIME type; <c>application/octet-stream</c> when null/blank.</param>
    /// <param name="length">The optional content length, letting the request carry <c>Content-Length</c> instead of chunked encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fileName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="openContent"/> is null.</exception>
    public MailgunFile(string fileName, System.Func<System.IO.Stream> openContent, string? contentType = null, long? length = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new System.ArgumentException("A file name is required.", nameof(fileName));
        }

        OpenContent = openContent ?? throw new System.ArgumentNullException(nameof(openContent));
        FileName = fileName;
        ContentType = contentType;
        Length = length;
    }

    /// <summary>Gets the raw file bytes, or <see langword="null"/> for a stream-backed file.</summary>
    public byte[]? Content { get; }

    /// <summary>Gets the stream factory, or <see langword="null"/> for a byte-array file.</summary>
    public System.Func<System.IO.Stream>? OpenContent { get; }

    /// <summary>Gets the declared length of a stream-backed file, when known.</summary>
    public long? Length { get; }
```

Док класса: упомянуть оба режима.

- [ ] **Step 4: HttpContent на фабрике**

`src/Mailgunner/Internal/StreamFactoryContent.cs`:

```csharp
namespace Mailgunner.Internal;

/// <summary>
/// An <see cref="System.Net.Http.HttpContent"/> that opens a fresh stream from a factory each time it is
/// serialized, so the same part can be sent again on a retry without buffering the whole file.
/// </summary>
internal sealed class StreamFactoryContent : System.Net.Http.HttpContent
{
    private readonly System.Func<System.IO.Stream> _open;
    private readonly long? _length;

    public StreamFactoryContent(System.Func<System.IO.Stream> open, long? length)
    {
        _open = open;
        _length = length;
    }

    protected override async System.Threading.Tasks.Task SerializeToStreamAsync(
        System.IO.Stream stream, System.Net.TransportContext? context)
    {
        using var source = _open();
        await source.CopyToAsync(stream).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length ?? 0;
        return _length.HasValue;
    }
}
```

(`SerializeToStreamAsync(Stream, TransportContext?)` существует на обоих TFM; на net8.0 перегрузка с `CancellationToken` не обязательна.)

`MailgunOptionsContent.AddFile`:

```csharp
    private static void AddFile(System.Net.Http.MultipartFormDataContent content, string field, MailgunFile file)
    {
        System.Net.Http.HttpContent fileContent = file.OpenContent is { } open
            ? new StreamFactoryContent(open, file.Length)
            : new System.Net.Http.ByteArrayContent(file.Content!);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? DefaultContentType : file.ContentType!);
        content.Add(fileContent, field, file.FileName);
    }
```

- [ ] **Step 5: Прогнать всё, README, CHANGELOG, коммит**

README, пункт «Attachments & inline files»: добавить «or `MailgunFile(fileName, () => File.OpenRead(path), contentType)` to stream large files without buffering; the factory is called once per request».
CHANGELOG `### Added`: `- Stream-backed MailgunFile(fileName, Func<Stream>, contentType, length); Content is now nullable for such files.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: stream-backed attachments via MailgunFile stream factory"
```

---

### Task 18: Проверка свежести timestamp в `MailgunWebhookSignature`

**Files:**
- Modify: `src/Mailgunner/MailgunWebhookSignature.cs`
- Test: `tests/Mailgunner.Tests/Webhooks/WebhookFreshnessTests.cs` (создать)

**Interfaces:**
- Produces: `static bool MailgunWebhookSignature.Verify(string signingKey, string timestamp, string token, string signature, TimeSpan maxAge, TimeProvider? timeProvider = null)`.

- [ ] **Step 1: Тесты**

`tests/Mailgunner.Tests/Webhooks/WebhookFreshnessTests.cs`:

```csharp
using System.Globalization;
using Mailgunner.Tests.Fakes;
using Xunit;

namespace Mailgunner.Tests.Webhooks;

public class WebhookFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static (string Timestamp, string Signature) Signed(long unixSeconds)
    {
        var ts = unixSeconds.ToString(CultureInfo.InvariantCulture);
        return (ts, WebhookTestVectors.Sign(WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token));
    }

    [Fact]
    public void A_recent_valid_signature_is_accepted()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() - 30);

        Assert.True(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_stale_valid_signature_is_rejected()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() - 600);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_far_future_timestamp_is_rejected()
    {
        var (ts, sig) = Signed(Now.ToUnixTimeSeconds() + 600);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("1.5")]
    public void A_non_integer_timestamp_is_rejected(string timestamp)
    {
        var sig = WebhookTestVectors.Sign(WebhookTestVectors.SigningKey, timestamp, WebhookTestVectors.Token);

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, timestamp, WebhookTestVectors.Token, sig, TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void A_forged_signature_is_rejected_even_when_fresh()
    {
        var (ts, _) = Signed(Now.ToUnixTimeSeconds());

        Assert.False(MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, ts, WebhookTestVectors.Token, new string('0', 64), TimeSpan.FromMinutes(5), new RecordingTimeProvider(Now)));
    }

    [Fact]
    public void Non_positive_max_age_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MailgunWebhookSignature.Verify(
            WebhookTestVectors.SigningKey, "1", WebhookTestVectors.Token, new string('0', 64), TimeSpan.Zero));
    }
}
```

- [ ] **Step 2: Убедиться что не компилируется.**

- [ ] **Step 3: Реализация**

В `MailgunWebhookSignature` добавить перегрузку:

```csharp
    /// <summary>
    /// Verifies the signature exactly as <see cref="Verify(string, string, string, string)"/> and additionally
    /// requires the webhook's <paramref name="timestamp"/> (Unix seconds) to be within
    /// <paramref name="maxAge"/> of the current time in either direction, which defeats replay of an old
    /// capture. Both checks always run; the result is authentic only when both pass.
    /// </summary>
    /// <param name="signingKey">The Mailgun HTTP webhook signing key. Required.</param>
    /// <param name="timestamp">The webhook's timestamp field, Unix seconds (untrusted input).</param>
    /// <param name="token">The webhook's token field (untrusted input).</param>
    /// <param name="signature">The webhook's hex signature field (untrusted input).</param>
    /// <param name="maxAge">The largest accepted distance between <paramref name="timestamp"/> and now. Must be positive.</param>
    /// <param name="timeProvider">The clock to use; <see cref="TimeProvider.System"/> when null.</param>
    /// <returns><see langword="true"/> when the signature is valid and the timestamp is fresh; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="signingKey"/> is null, empty, or whitespace.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="maxAge"/> is not positive.</exception>
    public static bool Verify(
        string signingKey,
        string timestamp,
        string token,
        string signature,
        System.TimeSpan maxAge,
        System.TimeProvider? timeProvider = null)
    {
        if (maxAge <= System.TimeSpan.Zero)
        {
            throw new System.ArgumentOutOfRangeException(nameof(maxAge), maxAge, "The maximum age must be positive.");
        }

        // Evaluate the signature first so the key precondition is enforced identically to the base overload.
        var authentic = Verify(signingKey, timestamp, token, signature);

        if (timestamp is null
            || !long.TryParse(timestamp, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        var now = (timeProvider ?? System.TimeProvider.System).GetUtcNow();
        var issued = System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var distance = now > issued ? now - issued : issued - now;

        return authentic && distance <= maxAge;
    }
```

`long.TryParse` с `NumberStyles.None` отвергает знак, пробелы и дробную часть. `FromUnixTimeSeconds` бросает `ArgumentOutOfRangeException` для значений вне диапазона DateTimeOffset; обернуть вызов в `try { } catch (System.ArgumentOutOfRangeException) { return false; }`.

- [ ] **Step 4: Прогнать всё, README, CHANGELOG, коммит**

README, «Webhook signature verification»: заменить пункт про replay на «Pass `maxAge` (e.g. `TimeSpan.FromMinutes(5)`) to the second overload to also reject stale or future timestamps; token-reuse tracking remains yours.» и показать вызов с `maxAge`.
CHANGELOG `### Added`: `- MailgunWebhookSignature.Verify overload with maxAge (and optional TimeProvider) rejecting stale/future timestamps.`

```bash
rtk git add src tests README.md CHANGELOG.md
rtk git commit -m "feat: timestamp freshness check for webhook signature verification"
```

---

## Фаза 6. Релиз

### Task 19: Тесты сборки `netstandard2.0` на .NET Framework 4.8

Сейчас netstandard-сборка никогда не исполняется, а под `#if` лежат ручной `FixedTimeEquals`, `ThreadStatic Random`, чтение тела. Отдельный небольшой проект на `net48`, запускаемый только на Windows-раннере.

**Files:**
- Create: `tests/Mailgunner.NetFxTests/Mailgunner.NetFxTests.csproj`
- Create: `tests/Mailgunner.NetFxTests/NetFxStubHandler.cs`
- Create: `tests/Mailgunner.NetFxTests/NetStandardBuildTests.cs`
- Modify: `Mailgunner.slnx`, `.github/workflows/ci.yml`, `.editorconfig`

- [ ] **Step 1: Проект**

`tests/Mailgunner.NetFxTests/Mailgunner.NetFxTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <!-- Resolves the netstandard2.0 asset of the library, exercising the #if !NET8_0_OR_GREATER branches. -->
    <ProjectReference Include="..\..\src\Mailgunner\Mailgunner.csproj" />
  </ItemGroup>

</Project>
```

В `Mailgunner.slnx` в папку `/tests/` добавить `<Project Path="tests/Mailgunner.NetFxTests/Mailgunner.NetFxTests.csproj" />`. В `.editorconfig` продублировать секцию для test-проектов (`IDE0005 = suggestion`) для пути `tests/Mailgunner.NetFxTests/**`, если секция задана по конкретному пути, а не по `tests/**`.

- [ ] **Step 2: Стаб**

`tests/Mailgunner.NetFxTests/NetFxStubHandler.cs`:

```csharp
using System.Net;

namespace Mailgunner.NetFxTests;

/// <summary>Minimal scripted transport: returns the queued (status, body) pairs in order and records each request body.</summary>
internal sealed class NetFxStubHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

    public NetFxStubHandler(params (HttpStatusCode Status, string Body)[] responses) =>
        _responses = new Queue<(HttpStatusCode, string)>(responses);

    public List<string> Bodies { get; } = new();

    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(body), RequestMessage = request };
    }
}
```

- [ ] **Step 3: Тесты**

`tests/Mailgunner.NetFxTests/NetStandardBuildTests.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailgunner.NetFxTests;

public class NetStandardBuildTests
{
    private const string Key = "netfx-test-signing-key";

    private static IMailgunnerClient BuildClient(NetFxStubHandler stub, Action<MailgunnerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMailgunner(o =>
        {
            o.Domain = "mg.example.com";
            o.SendingKey = "key-123";
            o.Region = MailgunRegion.Us;
            o.Retry.BaseDelay = TimeSpan.FromMilliseconds(1);
            configure?.Invoke(o);
        }).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }

    private static string Sign(string timestamp, string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + token))).Replace("-", string.Empty).ToLowerInvariant();
    }

    [Fact]
    public void Manual_fixed_time_compare_accepts_a_valid_signature_and_rejects_a_tampered_one()
    {
        var signature = Sign("1529006854", "tok");

        Assert.True(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", signature));
        Assert.False(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", "0" + signature.Substring(1)));
        Assert.False(MailgunWebhookSignature.Verify(Key, "1529006854", "tok", signature.Substring(1)));
    }

    [Fact]
    public async Task Send_round_trips_through_the_netstandard_build()
    {
        var stub = new NetFxStubHandler((HttpStatusCode.OK, "{\"id\":\"<1@mg>\",\"message\":\"Queued.\"}"));
        var client = BuildClient(stub);
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");

        var result = await client.SendAsync(message);

        Assert.Equal("<1@mg>", result.Id);
        Assert.Contains("name=to", stub.Bodies[0]);
    }

    [Fact]
    public async Task Retry_with_thread_static_random_jitter_works_on_net_framework()
    {
        var stub = new NetFxStubHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}"),
            (HttpStatusCode.OK, "{\"items\":[{\"address\":\"a@x.com\",\"created_at\":\"Thu, 11 Dec 2025 01:49:40 UTC\"}],\"paging\":{}}"));
        var client = BuildClient(stub);

        var page = await client.Suppressions.Bounces.ListPageAsync();

        Assert.Equal(2, stub.Requests);
        Assert.Equal(new DateTimeOffset(2025, 12, 11, 1, 49, 40, TimeSpan.Zero), page.Items[0].CreatedAt);
    }

    [Fact]
    public async Task Safe_send_mode_marks_requests_via_the_properties_bag()
    {
        var stub = new NetFxStubHandler((HttpStatusCode.ServiceUnavailable, "{\"message\":\"busy\"}"));
        var client = BuildClient(stub);
        var message = new MailgunMessage { From = "noreply@mg.example.com", Text = "Hi" };
        message.To.Add("alice@example.com");

        await Assert.ThrowsAsync<MailgunnerException>(() => client.SendAsync(message));

        Assert.Equal(1, stub.Requests);
    }
}
```

- [ ] **Step 4: Локальный прогон (Windows)**

Run: `rtk dotnet test tests/Mailgunner.NetFxTests -c Release -v q`
Expected: 4 теста PASS. На Linux этот проект собирать не нужно: он исключается из CI-шага ниже.

- [ ] **Step 5: CI-матрица**

В `.github/workflows/ci.yml` заменить job:

```yaml
jobs:
  build-test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - name: Checkout
        uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          global-json-file: global.json

      - name: Restore
        run: dotnet restore

      - name: Audit dependencies for known vulnerabilities
        if: runner.os == 'Linux'
        shell: bash
        run: |
          report=$(dotnet list package --vulnerable --include-transitive)
          echo "$report"
          if echo "$report" | grep -q 'has the following vulnerable packages'; then
            echo "::error::Vulnerable NuGet packages detected (see the audit output above)."
            exit 1
          fi

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test (net8.0)
        run: dotnet test tests/Mailgunner.Tests --configuration Release --no-build

      - name: Test (net48, netstandard2.0 asset)
        if: runner.os == 'Windows'
        run: dotnet test tests/Mailgunner.NetFxTests --configuration Release --no-build
```

На Linux `dotnet build` решения соберёт и net48-проект (для сборки достаточно reference assemblies, они подтягиваются пакетом `Microsoft.NETFramework.ReferenceAssemblies` автоматически SDK). Если сборка на Linux падает, добавить в `Mailgunner.NetFxTests.csproj` `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />` с версией в `Directory.Packages.props` (`1.0.3`).

- [ ] **Step 6: README, CHANGELOG, коммит**

README «Project layout»: строка `| \`tests/Mailgunner.NetFxTests/\` | net48 tests exercising the netstandard2.0 build (Windows CI leg). |`.
CHANGELOG `### Added`: `- The netstandard2.0 build is now executed by a net48 test project on the Windows CI leg.`

```bash
rtk git add tests/Mailgunner.NetFxTests Mailgunner.slnx .github/workflows/ci.yml .editorconfig Directory.Packages.props README.md CHANGELOG.md
rtk git commit -m "test: run the netstandard2.0 build on .NET Framework 4.8 in CI"
```

---

### Task 20: Release workflow с тестами и package validation

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `src/Mailgunner/Mailgunner.csproj`
- Modify: `docs/RELEASING.md`

- [ ] **Step 1: Тесты перед pack**

В `release.yml` между «Setup .NET» и «Pack» вставить:

```yaml
      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test tests/Mailgunner.Tests --configuration Release --no-build
```

и у шага Pack добавить `--no-build`.

- [ ] **Step 2: Package validation**

В `src/Mailgunner/Mailgunner.csproj` в первый `PropertyGroup`:

```xml
    <!-- Validates the net8.0 and netstandard2.0 surfaces against each other on pack. After the first
         stable release add <PackageValidationBaselineVersion> to also catch breaking changes. -->
    <EnablePackageValidation>true</EnablePackageValidation>
```

Run: `rtk dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o artifacts` → без ошибок `CP*`. Удалить `artifacts/` после проверки (папка не должна попасть в коммит; при необходимости добавить `artifacts/` в `.gitignore`).

- [ ] **Step 3: RELEASING.md**

В «Cutting a release» шаг 3 дополнить: «The workflow builds and runs the offline test suite first; a red test suite blocks the pack and push.» В «Versioning notes» добавить: «After the first stable tag, set `PackageValidationBaselineVersion` in `Mailgunner.csproj` to that version so later packs fail on breaking API changes.»

- [ ] **Step 4: Коммит**

```bash
rtk git add .github/workflows/release.yml src/Mailgunner/Mailgunner.csproj docs/RELEASING.md .gitignore
rtk git commit -m "ci: gate release on tests and enable package validation"
```

---

### Task 21: Интеграционные тесты против живого Mailgun (пропуск без ключей)

Три блокера жили там, где не было живой проверки. Проект `tests/Mailgunner.IntegrationTests`: каждый тест выходит без падения, если переменные окружения `Mailgun__Domain`, `Mailgun__SendingKey`, `Mailgun__Region` не заданы. В CI не запускается (нет секретов), запускается вручную разработчиком.

**Files:**
- Create: `tests/Mailgunner.IntegrationTests/Mailgunner.IntegrationTests.csproj`
- Create: `tests/Mailgunner.IntegrationTests/Live.cs`
- Create: `tests/Mailgunner.IntegrationTests/SuppressionsLiveTests.cs`
- Create: `tests/Mailgunner.IntegrationTests/WebhooksLiveTests.cs`
- Create: `tests/Mailgunner.IntegrationTests/SendLiveTests.cs`
- Modify: `Mailgunner.slnx`, `README.md`, `.editorconfig`

- [ ] **Step 1: Проект**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Mailgunner\Mailgunner.csproj" />
  </ItemGroup>

</Project>
```

Добавить в `Mailgunner.slnx` (папка `/tests/`) и продублировать секцию `.editorconfig` для тестов, как в Task 19.

- [ ] **Step 2: Резолвер окружения**

`tests/Mailgunner.IntegrationTests/Live.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Mailgunner.IntegrationTests;

/// <summary>
/// Reads live credentials from the environment. When any is absent, <see cref="Client"/> is null and every
/// test returns early, so the suite is green with no secrets and never runs in CI.
/// </summary>
internal static class Live
{
    public static readonly string? Domain = Env("Mailgun__Domain");
    public static readonly string? Recipient = Env("Mailgun__Recipients__0__Address");

    public static readonly IMailgunnerClient? Client = Build();

    private static IMailgunnerClient? Build()
    {
        var key = Env("Mailgun__SendingKey");
        var region = Env("Mailgun__Region");
        if (Domain is null || key is null || region is null || !Enum.TryParse<MailgunRegion>(region, ignoreCase: true, out var parsed))
        {
            return null;
        }

        var services = new ServiceCollection();
        services.AddMailgunner(Domain, key, parsed);
        return services.BuildServiceProvider().GetRequiredService<IMailgunnerClient>();
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
```

- [ ] **Step 3: Тесты**

`SuppressionsLiveTests.cs`:

```csharp
using Xunit;

namespace Mailgunner.IntegrationTests;

public class SuppressionsLiveTests
{
    [Fact]
    public async Task Bounce_add_get_list_remove_round_trip()
    {
        if (Live.Client is not { } client) { return; }
        var address = $"live-{Guid.NewGuid():N}@example.com";

        await client.Suppressions.Bounces.AddAsync(new Bounce { Address = address, Code = "550", Error = "live test" });
        var fetched = await client.Suppressions.Bounces.GetAsync(address);
        Assert.Equal(address, fetched.Address);
        Assert.NotNull(fetched.CreatedAt); // "UTC" timestamps must parse

        var listed = new List<Bounce>();
        await foreach (var b in client.Suppressions.Bounces.ListAsync(pageSize: 1000)) { listed.Add(b); }
        Assert.Contains(listed, b => b.Address == address);

        await client.Suppressions.Bounces.RemoveAsync(address);
        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Suppressions.Bounces.GetAsync(address));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_add_range_and_clear_entries()
    {
        if (Live.Client is not { } client) { return; }
        var a = $"live-{Guid.NewGuid():N}@example.com";
        var b = $"live-{Guid.NewGuid():N}@example.com";

        await client.Suppressions.Unsubscribes.AddRangeAsync(new[]
        {
            new Unsubscribe { Address = a, Tags = new[] { "*" } },
            new Unsubscribe { Address = b, Tags = new[] { "newsletter" } },
        });

        Assert.Equal(a, (await client.Suppressions.Unsubscribes.GetAsync(a)).Address);
        await client.Suppressions.Unsubscribes.RemoveAsync(a);
        await client.Suppressions.Unsubscribes.RemoveAsync(b);
    }
}
```

`WebhooksLiveTests.cs`:

```csharp
using Xunit;

namespace Mailgunner.IntegrationTests;

public class WebhooksLiveTests
{
    [Fact]
    public async Task Create_get_update_list_delete_round_trip()
    {
        if (Live.Client is not { } client) { return; }
        const WebhookEventType type = WebhookEventType.TemporaryFail;
        var url = $"https://example.com/hooks/{Guid.NewGuid():N}";

        try { await client.Webhooks.DeleteAsync(type); } catch (MailgunnerException ex) when (ex.StatusCode == 404) { }

        var created = await client.Webhooks.CreateAsync(type, new[] { url });
        Assert.Contains(url, created.Urls);

        var updated = await client.Webhooks.UpdateAsync(type, new[] { url + "/v2" });
        Assert.Contains(url + "/v2", updated.Urls);

        var listed = await client.Webhooks.ListAsync();
        Assert.Contains(listed, r => r.EventType == type);

        await client.Webhooks.DeleteAsync(type);
        var ex = await Assert.ThrowsAsync<MailgunnerException>(() => client.Webhooks.GetAsync(type));
        Assert.Equal(404, ex.StatusCode);
    }
}
```

`SendLiveTests.cs` (test mode, ничего не доставляется):

```csharp
using Xunit;

namespace Mailgunner.IntegrationTests;

public class SendLiveTests
{
    [Fact]
    public async Task Test_mode_send_is_accepted()
    {
        if (Live.Client is not { } client || Live.Recipient is null) { return; }
        var message = new MailgunMessage
        {
            From = $"postmaster@{Live.Domain}",
            Subject = "Mailgunner live check",
            Text = "test mode, not delivered",
        };
        message.To.Add(Live.Recipient);
        message.Options.TestMode = true;

        var result = await client.SendAsync(message);

        Assert.False(string.IsNullOrEmpty(result.Id));
    }
}
```

- [ ] **Step 4: Прогон без ключей и с ключами**

Run: `rtk dotnet test tests/Mailgunner.IntegrationTests -c Release -v q` → PASS (4 теста, все ранний выход).
С ключами (те же переменные, что у sample в README) прогнать ещё раз; ожидание: PASS. Любое падение здесь это реальное расхождение с API, чинить в библиотеке, не в тесте.

- [ ] **Step 5: README, коммит**

README «Building from source»: абзац «Live integration tests (`tests/Mailgunner.IntegrationTests`) run only when the `Mailgun__*` variables from the sample section are set; without them every test returns early and the suite stays green.»

```bash
rtk git add tests/Mailgunner.IntegrationTests Mailgunner.slnx .editorconfig README.md
rtk git commit -m "test: environment-gated live integration tests for sends, suppressions and webhooks"
```

---

### Task 22: Убрать избыточные квалификаторы имён в `src/`

Косметика с нулевым риском для поведения, поэтому в самом конце. `ImplicitUsings` уже даёт `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`.

**Files:**
- Modify: все `src/Mailgunner/**/*.cs`

- [ ] **Step 1: Попробовать автоматический путь**

Временно добавить в `.editorconfig` (секция `[*.cs]`): `dotnet_diagnostic.IDE0001.severity = warning`, затем:

```bash
rtk dotnet format style Mailgunner.slnx --diagnostics IDE0001 --severity warn
```

Вернуть `.editorconfig` к исходному состоянию (`rtk git checkout .editorconfig`).

- [ ] **Step 2: Ручной путь, если автоматический не сработал**

По одному файлу: удалять префиксы `System.Threading.Tasks.`, `System.Collections.Generic.`, `System.Threading.`, `System.Net.Http.` (кроме `System.Net.Http.Headers.` → заменять на `Headers.` нельзя; писать `using System.Net.Http.Headers;` и голое имя) и `System.` перед `Uri`, `TimeSpan`, `Math`, `Array`, `Func`, `Action`, `Exception`-типами. Не трогать `System.Text.Json.*`, `System.Globalization.*`, `System.Security.Cryptography.*` (не implicit) либо добавить для них явный `using`. После каждого файла:

```bash
rtk dotnet build src/Mailgunner -c Release
```

- [ ] **Step 3: Полный прогон и коммит**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build` → 0 warnings, PASS.

```bash
rtk grep -rn "System\.Threading\.Tasks\.Task\b" src/Mailgunner   # ожидается пусто
rtk git add src/Mailgunner
rtk git commit -m "style: drop redundant namespace qualifiers under implicit usings"
```

---

### Task 23: Документация ограничений (AOT, дубли, таймауты)

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Раздел «Limitations & notes»**

Перед «Building from source» добавить:

```markdown
## Limitations & notes

- **No trimming/AOT guarantee.** Template and recipient variables (`t:variables`, `recipient-variables`) are
  serialized with reflection-based `System.Text.Json`; in a Native AOT app that path throws at runtime. The
  suppression and webhook DTOs use source generation and are unaffected.
- **Duplicate delivery vs. retries.** A send is retried only on HTTP 429 by default (`SendRetryMode.Safe`); with
  `SendRetryMode.Full` a lost response can lead to the same message being delivered twice.
- **Timeouts.** Each attempt is bounded by `Retry.AttemptTimeout`; the typed `HttpClient.Timeout` is infinite.
  The worst-case wall time of one call is `(MaxRetryAttempts + 1) × AttemptTimeout + Σ waits`.
- **Batch failures.** `SendBatchAsync` is fail-fast; `MailgunnerException.AcceptedResults` / `FailedChunkIndex`
  tell you which chunks were already accepted so you can resume from the failed one.
- **16KB parameter cap** on `o:`/`h:`/`v:`/`t:` fields is not enforced client-side (see Send options).
```

- [ ] **Step 2: Коммит**

```bash
rtk git add README.md
rtk git commit -m "docs: document AOT, retry, timeout and batch-failure limitations"
```

---

### Task 24: Срез версии `0.2.0` и согласование README/CHANGELOG

**Files:**
- Modify: `README.md:6-9`, `README.md:24-27`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: README status**

Заменить блок `> **Status:** ...` на:

```markdown
> **Status:** `0.2.0` — second release. Adds named clients, one-click List-Unsubscribe, domain webhook
> management, inline-body batches, stream attachments, a safe-by-default send retry mode, and fixes the
> webhook path, suppression timestamps and JSON add bodies found in review. See the
> [changelog](https://github.com/gberikov/Mailgunner/blob/master/CHANGELOG.md).
```

Заменить блок под «Installation» (`> Published to NuGet on tagging v0.1.0; until then...`) на `> Pre-releases are published on `v*` tags; see [docs/RELEASING.md](docs/RELEASING.md).`

- [ ] **Step 2: CHANGELOG**

Переименовать `## [Unreleased]` в `## [0.2.0] - <дата коммита в формате YYYY-MM-DD>` и создать пустую `## [Unreleased]` над ней. Внизу обновить ссылки:

```
[Unreleased]: https://github.com/gberikov/Mailgunner/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/gberikov/Mailgunner/compare/v0.1.0-preview.1...v0.2.0
[0.1.0-preview.1]: https://github.com/gberikov/Mailgunner/releases/tag/v0.1.0-preview.1
[0.1.0]: https://github.com/gberikov/Mailgunner/releases/tag/v0.1.0
```

Проверить, что каждая задача 1–23 оставила свою строку в секциях `Added/Changed/Fixed/Security` (список: Tasks 1, 2, 3, 4, 5, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19). Недостающие дописать.

- [ ] **Step 3: Финальная проверка и коммит**

Run: `rtk dotnet build Mailgunner.slnx -c Release && rtk dotnet test Mailgunner.slnx -c Release --no-build && rtk dotnet pack src/Mailgunner/Mailgunner.csproj -c Release -o artifacts`
Expected: 0 warnings, PASS, пакет собран. Удалить `artifacts/`.

```bash
rtk git add README.md CHANGELOG.md
rtk git commit -m "docs: cut 0.2.0 changelog and align README status"
```

Тег `v0.2.0` ставит пользователь вручную по `docs/RELEASING.md`; этот план тег не создаёт.

---

## Вне этого плана: следующие спеки

Каждый пункт это отдельная фича со своим spec/plan (speckit `specs/NNN-...`), не правка:

1. **Mailing lists и members** (`/v3/lists`, `/v3/lists/{address}/members`): CRUD списков, bulk-загрузка членов, отправка на список.
2. **Events API** (`GET /v3/{domain}/events`): фильтры, курсорная пагинация, типизированная модель события.
3. **Templates CRUD** (`/v3/{domain}/templates` и версии).
4. **Whitelists** (`/v3/{domain}/whitelists`) и CSV-импорт suppressions (`/import`).
5. **Webhooks v4** (`/v4/domains/{domain}/webhooks`, один URL на много событий атомарно) и account-level webhooks.
6. **`messages.mime`**, stored messages (`GET /v3/domains/{domain}/messages/{key}`), sending queues.
7. **Типизированный payload вебхука** (`signature` + `event-data`) поверх `MailgunWebhookSignature`.
8. **Параллельная отправка чанков** батча с ограничением степени параллелизма.
9. **Email validation v4**, stats/metrics, tags, domains, IPs, routes.

---

## Self-review (выполнен автором плана)

- **Покрытие ревью:** блокеры 1–4 → Tasks 1–4; инъекция адресов → 7; экранирование домена → 8; дубли при ретраях → 9; `HttpClient.Timeout` → 10; переполнение backoff → 11; частичные результаты батча и текст ошибки → 12; ReplyTo/Options → 13; опции отправки/AMP → 14; inline-батч → 15; `accepted` → 16; Stream-вложения → 17; freshness подписи → 18; netstandard-тесты → 19; release gate и package validation → 20; интеграционные тесты → 21; квалификаторы имён и дубли кода → 6 и 22; Polly.Core и версии зависимостей → 5; AOT/лимиты в доках → 23; README/CHANGELOG → 24; крупные пробелы API → раздел «следующие спеки».
- **Согласованность имён:** `MailgunHttp.SendAsync`/`AddField` (Task 6) используются в 9, 13, 14, 15, 17; `SendRetryMode` (Task 9) в 10, 19, 23; `AttemptTimeout` (Task 10) в 23; `MailgunBatchContent.Chunk<T>` (Task 3) в 12; `StubHttpMessageHandler.BeforeResponse` (Task 10) только в 10; `AcceptedResults`/`FailedChunkIndex` (Task 12) в 23.
- **Зависимости между задачами:** 6 раньше 9/13/14/15/17 (хелперы); 9 раньше 10 (тест safe-режима с таймаутом) и 19 (net48-тест safe-режима); 2 и 3 раньше 19 и 21 (проверяют `UTC` и массив); 5 раньше всех остальных (итоговый набор пакетов).

