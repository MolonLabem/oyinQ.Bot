namespace oyinQ.Bot.Features.PublicSite;

public static class PrivacyPolicyPage
{
    public const string Path = "/privacy";
    public const string LastUpdated = "31 августа 2026 года";

    public static async Task HandleAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(BuildHtml(), context.RequestAborted);
    }

    public static string BuildHtml() => $$"""
        <!doctype html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="light dark">
          <title>Политика конфиденциальности — OyinQ</title>
          <style>
            :root { color-scheme: light dark; --bg:#f4f6f8; --surface:#fff; --text:#18202b; --muted:#637083; --accent:#3978d4; --line:#dce2ea; }
            @media (prefers-color-scheme:dark) { :root { --bg:#111318; --surface:#1b1f27; --text:#f2f4f7; --muted:#aab3c1; --accent:#78aaf0; --line:#303744; } }
            * { box-sizing:border-box; }
            body { margin:0; background:var(--bg); color:var(--text); font:16px/1.62 system-ui,-apple-system,"Segoe UI",sans-serif; }
            main { width:min(820px,calc(100% - 28px)); margin:28px auto 64px; padding:clamp(22px,5vw,48px); background:var(--surface); border:1px solid var(--line); border-radius:20px; box-shadow:0 18px 55px rgb(0 0 0 / .08); }
            h1 { margin:0 0 6px; font-size:clamp(1.75rem,5vw,2.55rem); line-height:1.15; }
            h2 { margin:32px 0 8px; font-size:1.2rem; line-height:1.3; }
            p,ul { margin:8px 0; } li+li { margin-top:5px; }
            .brand { color:var(--accent); font-weight:800; letter-spacing:.02em; }
            .updated { color:var(--muted); }
            a { color:var(--accent); }
          </style>
        </head>
        <body>
          <main>
            <div class="brand">OyinQ</div>
            <h1>Политика конфиденциальности</h1>
            <p class="updated">Последнее обновление: {{LastUpdated}}</p>

            <p>OyinQ помогает Telegram-клубам и кэмпам организовывать настольные игры. Эта страница описывает данные, которые фактически нужны приложению для работы.</p>

            <h2>Telegram-идентичность и доступ</h2>
            <p>OyinQ может хранить Telegram ID пользователя, username при его наличии, отображаемое имя и выбранное пользователем предпочтительное имя. Telegram ID используется как основная учётная и авторизационная идентичность.</p>
            <p>Для доступа к настроенным клубам и кэмпам OyinQ проверяет через Telegram, состоит ли пользователь в соответствующей группе. Данные Mini App принимаются только после проверки Telegram initData.</p>

            <h2>Регистрация на кэмп</h2>
            <p>При регистрации могут сохраняться город, выбор необходимости проживания, выбранные даты присутствия и служебные сведения о регистрации. Пользователь может изменять эти данные или отменить регистрацию в Mini App. Отмена удаляет данные текущего участия и будущие записи, но не стирает историю других сообществ и уже состоявшихся событий.</p>

            <h2>BoardGameGeek</h2>
            <p>Если пользователь запускает импорт BGG, OyinQ обрабатывает указанный BGG username, сведения о коллекции настольных игр, а также выбранные игры и дополнения, которые пользователь решил добавить в кэмп. API-токен BGG принадлежит серверной интеграции OyinQ и не является пользовательскими данными.</p>

            <h2>Сборы и доступность игр</h2>
            <p>OyinQ хранит созданные сборы, организаторов, подтверждённых участников, очередь ожидания, отмены и историю, необходимую для работы продукта. Также хранится информация о том, какой участник может или обязуется привезти игру в кэмп.</p>

            <h2>Хранение и инфраструктура</h2>
            <p>Прикладные данные хранятся в PostgreSQL и обрабатываются инфраструктурой, необходимой для работы OyinQ и его Telegram-интеграции. Данные клуба или кэмпа сохраняются, пока нужны для работы сообщества. Успешные и отменённые сборы могут оставаться в истории. Не набравшие минимум участников прошедшие сборы удаляются в соответствии с текущим поведением продукта. Временные задания импорта могут очищаться. Часть устаревшей схемы может сохраняться для безопасной миграции данных.</p>

            <h2>Передача и публикация</h2>
            <p>OyinQ не предназначен для публикации пользовательских данных вне соответствующего клуба или кэмпа. Имена, участие, доступность игр и связанные сведения могут появляться в Mini App, объявлениях и уведомлениях Telegram, когда это необходимо для функций продукта. Telegram и BoardGameGeek обрабатывают данные по собственным правилам своих сервисов.</p>

            <h2>Действия пользователя и обращения</h2>
            <p>В Mini App можно редактировать профильное имя, регистрацию на кэмп, список привозимых игр и участие в будущих сборах в пределах доступных функций. Для более широкого запроса на удаление или вопросов о конфиденциальности обратитесь к администраторам вашего сообщества OyinQ или к администратору бота. Отдельный публичный адрес для таких обращений сейчас не настроен.</p>

            <p><a href="/app/">Открыть OyinQ</a></p>
          </main>
        </body>
        </html>
        """;
}
