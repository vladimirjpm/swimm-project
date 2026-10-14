# SwimHub — политика конфиденциальности и условия пользования: материалы для ревью (раунд 2)

**Что это за документ.** Подборка для внешнего ревью двух юридических страниц сайта SwimHub:
**политики конфиденциальности** (`/privacy`) и **условий пользования** (`/terms`). Ниже — контекст
(что за сайт, какие данные, как устроены регистрация и поиск), что изменено после первого раунда
ревью, вопросы и **полные тексты** на английском и иврите. Русская версия — перевод для владельца.

Тексты составлены 14.10.2026 ИИ-ассистентом (Claude) по просьбе владельца, в тот же день прошли
первый раунд ревью (ChatGPT) и переписаны по нему. **Юристом не проверены.** Сайт ещё не запущен.

---

## 1. Что за сайт

- **SwimHub** (`swimhub.co.il`) — бесплатный некоммерческий сайт с результатами соревнований по
  плаванию в Израиле и рекордами. Интерфейс — английский; имена пловцов и клубов — на иврите.
- **Оператор** — частное лицо в Израиле (Владимир Баренцев), владелец домена и базы. Юрлица нет.
  Рекламы, платных функций, продажи данных нет.
- **Аудитория** — пловцы (много детей и подростков), их родители, тренеры, мастерсы.
- **Поставщики:** Microsoft Azure (Israel Central) — хостинг и база; Resend (американская компания,
  регион отправки — ЕС) — служебные письма, трекинг открытий/кликов выключен; Google — вход OAuth;
  Cloudflare — DNS и пересылка почты домена.

## 2. Какие данные обрабатываются

**А. О пловцах — из публичных источников** (у пловцов не собираются): официальные результаты
Федерации плавания Израиля и протоколы на loglig.com, списки рекордов World Aquatics. Поля: имя
(иврит/английский), год рождения, клуб, заплывы. Плюс наши расчёты: личные/сезонные лучшие, уровни,
места среди сверстников, сравнения. В базе ≈ 5 900 публичных пловцов, **≈ 4 900 из них — младше 18
или с неизвестным годом рождения**. Ошибки протокола мы не правим, а помечаем «Under review»; свои
ошибки (склейка однофамильцев, привязка результата) исправляем.

**Б. Владельцы аккаунтов** (добровольно): email, отображаемое имя, фото профиля Google, хэш пароля
(Argon2id), отметка «мне 13+ (и, если младше 18, есть разрешение родителя)» и её время, история
входов (дата, время, IP; ≈ 90 дней), добавленное самим пользователем: избранное, группы и их
участники, тренировки, ссылки на фото/видео (файлы не храним), реакции. Самостоятельного удаления
аккаунта в интерфейсе нет — по письму.

**В. Пловцы, добавленные тренером в группу** — люди без аккаунта и без результатов федерации. Видны
только участникам группы.

**Медиа:** видимость на выбор — только автор / участники группы / все. Публичные медиа видны на
открытых страницах и могут индексироваться. YouTube встраивается через `youtube-nocookie.com`.

## 3. Как устроены возраст и поиск

- **Аккаунт — с 13 лет по самодекларации:** «I am 13 or older. If I am under 18, I have permission
  from my parent or legal guardian to use SwimHub.» Без галочки аккаунт не создаётся — ни
  регистрацией, ни первым входом через Google. Документов не просим.
- **Индексируются** страницы соревнований, клубов и **только взрослых** пловцов. Взрослый — когда
  известен год рождения и `текущий год − год рождения ≥ 19` (то есть 18 исполнилось наверняка). Год
  неизвестен — считаем ребёнком. Страницы детей: `noindex` (мета-тег и заголовок `X-Robots-Tag`), их
  нет в sitemap. Имена детей остаются в результатах соревнований и клубов — как в протоколе.
- **«Скрыть из поиска» по просьбе** — для страницы любого пловца; результаты не удаляются.
- Группы, личное избранное — закрыты от поиска всегда (`robots.txt` + `noindex`).
- **Cookie:** только технические — сессия, временная при входе через Google, защита форм.

## 4. Что изменено после первого раунда ревью

Принято всё «критичное» и почти всё «желательное»:

1. **Получатели раскрыты полностью:** все посетители, поисковики, участники групп, выбранная
   аудитория медиа, поставщики, госорганы по закону (раньше было «только поставщики»).
2. **Медиа:** убрано ложное «медиа никогда не индексируются»; описаны уровни видимости; фраза о том,
   что видимость SwimHub не меняет настроек внешних сервисов.
3. **Исправления:** протокол федерации изменить не можем, а свои ошибки переноса, опознания и расчётов
   исправляем; запросы о неточных данных рассматриваем.
4. **Цель:** «архив соревнований и статистика» — без утверждения о цели федерации.
5. **Страницы детей сняты с индексации кодом** (было: все пловцы индексируются).
6. **«Скрыть из поиска»:** только страница профиля; имя остаётся в протоколах; сроки — у
   поисковиков; рассматриваем просьбы ограничить показ или удалить данные.
7. **Обработка вне Израиля (включая США)** раскрыта.
8. **«Родитель знает» → «есть разрешение родителя или законного опекуна»** — и в галочке, и в текстах.
9. **Группы:** согласие самого пловца, за несовершеннолетнего — родителя (раньше — «родителей» для всех).
10. **Удаление аккаунта** описано по факту (по письму; что удаляется; группы — по договорённости;
    результаты не удаляются; бэкапы — 14 дней).
11. **Сроки хранения:** добавлены журналы сервера, переписка; бэкапы.
12. **Права:** добавлено основание «неясные»; проверка личности и полномочий родителя; 30 дней — на
    доступ, остальное — как можно скорее.
13. **Cookie:** добавлена временная cookie входа через Google; YouTube nocookie; письма без трекинга.
14. **Изменения** действуют на будущее; новая цель — только с уведомлением/согласием по закону.
15. **Условия:** «берегите данные входа и сообщайте о подозрительном» вместо «отвечаете за всё»;
    фиксированная подсудность Тель-Авива убрана («определяется применимым законом»);
    «sexually explicit / בעל אופי מיני מפורש»; ответственность сужена (убран отказ за контент
    пользователей); иврит: «על האתר», «אינו מייצג אותו», убрано «золотое сердечко».

## 5. Вопросы ко второму раунду

1. Закрыты ли пункты первого раунда, нет ли новых противоречий между `/privacy` и `/terms`?
2. Достаточно ли закрытия страниц детей от индексации (при том что имена остаются в протоколах
   соревнований, которые индексируются) для снижения риска «цифрового следа»? Что ещё дёшево сделать?
3. Правило «взрослый наверняка = год ≥ 19 по году рождения» — разумно?
4. Формулировки о передаче за границу — достаточно ли их в тексте (договоры — отдельная тема)?
5. Остались ли пункты в условиях, рискованные по Закону о стандартных договорах?
6. Иврит: точность и естественность после правок.

Формат ответа: список проблем с серьёзностью (критично / желательно / стилистика) и предложенной
формулировкой на английском и иврите.

## 6. Где это живёт в коде

- Тексты: `client/src/content/help/{privacy,terms}/{en,ru,he}.md` (подмножество markdown;
  `[текст](route:terms)` — ссылка на страницу сайта).
- Страницы: `https://swimhub.co.il/privacy`, `https://swimhub.co.il/terms`.
- Правило индексации: `server/Swimm.Application/Mapping/SeoText.cs` (`IsCertainlyAdult`),
  sitemap — `server/Swimm.Infrastructure/Repositories/SeoRepository.cs`.

---

# Приложение A. Политика конфиденциальности — English (`/privacy`)

````markdown
# Privacy policy

Last updated: 14 October 2026.

SwimHub (swimhub.co.il) shows results of swimming competitions in Israel and swimming records. This policy explains what information we hold, why, who can see or receive it and what you can ask of us. Using the site is also governed by the [Terms of use](route:terms).

## Who we are

SwimHub is run by a private individual in Israel, Vladimir Barentsev, who is the controller of the site’s database. Contact for anything about your information: **privacy@swimhub.co.il**. General questions: info@swimhub.co.il.

## Information about swimmers

**Where it comes from.** Competition, swimmer and club pages are built from published sources: the official results of the Israel Swimming Association and the competition protocols it publishes on loglig.com, and the record lists of World Aquatics and national federations. We do not ask swimmers for this information.

**What it is.** A swimmer’s name (in Hebrew and, where available, in English), birth year, club and races — times, places, medals, points and split times, as printed in the protocol. From these results we calculate our own figures: personal and season bests, levels, places among swimmers of the same age, head-to-head comparisons.

**Why.** We use published competition results to provide searchable competition records and statistical summaries of sporting performance.

**Accuracy.** We show what the protocol says; where a protocol has an obvious error, the official figure stays and the line is marked “Under review”. We cannot amend the federation’s official protocol. We can correct errors in SwimHub’s copying, identification and calculations — for example a result attached to the wrong person or two people merged into one — and we will assess requests concerning inaccurate personal information displayed on our site.

## Information about account holders

You don’t need an account to look at results. **Giving us information for an account is voluntary — there is no legal obligation to do so.** Without an account you can’t keep favorites, join groups or add videos; everything else works.

What we keep and why:

- **email, display name** and, if you sign in with Google, your **Google profile picture** — to run your account and send account emails (confirming your address, resetting your password);
- a **password hash**, never the password itself, if you use email and password — to let you sign in;
- your **confirmation that you are 13 or older** (and, if under 18, that you have a parent’s or guardian’s permission) and when you gave it — see “Children”;
- **sign-in history** — date, time and IP address — to protect your account from misuse;
- **what you add yourself**: favorites, groups and their members, trainings, links to photos and videos, reactions — to show them to you and to the audience you choose.

We don’t use your information for advertising, we don’t sell it and we don’t pass it to anyone for marketing. We don’t make automated decisions about you.

## Who can see or receive information

- **Everyone.** Published competition information — results and the swimmer, club and competition pages built from them — is available to all visitors. Pages of competitions, clubs and adult swimmers may be indexed by search engines; see “Search engines”.
- **Group members.** Information shared within a group — its members, trainings, lanes, media — is available to that group’s members. Swimmers added to a group by a coach are visible only inside that group.
- **The audience you choose.** Media you add is visible at the level you choose: only you, a group’s members, or everyone. Media made public appears on public pages and can be found by search engines. Visibility settings on SwimHub do not change the privacy settings of linked content on external services — a YouTube video stays as public or private as it is on YouTube.
- **Authorities** — only when the law requires it.

We also use service providers that operate the site for us, each for its own task:

- **Microsoft Azure**, Israel Central region — hosting of the site and its database;
- **Resend** — sending account emails;
- **Google** — “Sign in with Google”; we receive only your name, email and profile picture;
- **Cloudflare** — the site’s domain name and email routing for swimhub.co.il.

Some of these providers process personal information outside Israel, including in the United States. This may include email addresses, email content and sign-in information.

## Children

- **You must be at least 13 to create an account. If you are under 18, you must have permission from a parent or legal guardian.** You confirm this when you sign up; we don’t ask for documents.
- Children under 13 don’t have their own account. A parent can follow their children’s results from the parent’s own account.
- **Profile pages of swimmers under 18 are not indexed by search engines** and are not in our sitemap. Their names still appear in competition and club results, as in the official protocol.
- Before adding a swimmer’s personal information or media to a group, a group member must obtain the swimmer’s permission and, for a minor, permission from their parent or legal guardian for the intended sharing.

## Search engines

Pages of competitions, clubs and adult swimmers can appear in search engines such as Google, and so can media its owner made public. Groups and your favorites never do; profile pages of swimmers under 18 are not indexed.

**To take a swimmer’s profile page out of search engines**, write to privacy@swimhub.co.il. We will tell search engines not to index the page and remove it from our sitemap. This request concerns the swimmer’s profile page. Their name may still appear on competition and club pages. Search engines control when their indexes are updated. We will also assess requests to restrict or remove personal information, taking account of applicable law and the circumstances.

## How long we keep information

- swimmers’ results — while the site exists, as part of the competition record;
- account information and what you added — until you remove it or your account is deleted;
- sign-in history — about 90 days;
- technical server logs (requests and errors, which may include IP addresses) — for the limited period needed to run and protect the site;
- emails you send us — as long as needed to handle your request;
- database backups — 14 days; information deleted from the site disappears from backups within that time.

## Deleting your account

Write to privacy@swimhub.co.il from the account’s email address. We delete the account details and what is linked only to you — favorites, your media links and reactions. For groups you manage we will agree with you whether to hand them over or delete them. Deleting an account does not remove published competition results.

## Your rights

Under the Israeli Protection of Privacy Law you may ask to see the information we hold about you and ask us to correct or delete information that is not correct, complete, clear or up to date. A parent or legal guardian may make requests for a minor. Before giving out or changing information we will take reasonable steps to confirm who is asking and, for a parent, their authority.

Write to **privacy@swimhub.co.il**. We give access to information within 30 days, as the law provides, and handle other requests as soon as we reasonably can. If you think we got it wrong, you may complain to the Privacy Protection Authority (gov.il).

## Cookies and your browser

We use only cookies the site needs: one that keeps you signed in, a temporary one during “Sign in with Google”, and a security cookie that protects forms. Display settings — light or dark mode, the language of explanations — are kept in your browser. We don’t use advertising or tracking cookies or third-party analytics, and account emails are sent without open or click tracking. Embedded videos use YouTube’s privacy-enhanced mode; profile pictures from Google are loaded from Google’s servers.

## Security

Connections to the site are encrypted, passwords are stored only as hashes, and access to the database is limited to the operator. No system is perfectly safe; if a breach puts your information at risk, we will act as the law requires, including notifying the authority and, where required, you.

## Changes

If we change this policy, the new version appears on this page with a new date and applies from then on. A significant change that affects account holders we will also announce on the site. We will not use information already collected for a substantially new purpose without the notice or consent the law requires.
````

---

# Приложение B. Политика конфиденциальности — עברית (`/privacy`)

````markdown
# מדיניות פרטיות

עודכן: 14 באוקטובר 2026.

SwimHub (swimhub.co.il) מציג תוצאות של תחרויות שחייה בישראל ושיאי שחייה. מדיניות זו מסבירה איזה מידע אנחנו מחזיקים, לשם מה, מי יכול לראות או לקבל אותו ומה אפשר לבקש מאיתנו. השימוש באתר כפוף גם ל[תנאי השימוש](route:terms).

## מי אנחנו

SwimHub מופעל על ידי אדם פרטי בישראל, ולדימיר ברנצב, שהוא בעל השליטה במאגר המידע של האתר. בכל עניין הנוגע למידע שלכם: **privacy@swimhub.co.il**. שאלות כלליות: info@swimhub.co.il.

## מידע על שחיינים

**מאיפה הוא מגיע.** דפי התחרויות, השחיינים והמועדונים נבנים ממקורות שפורסמו: התוצאות הרשמיות של איגוד השחייה בישראל ופרוטוקולי התחרויות שהוא מפרסם ב-loglig.com, ורשימות השיאים של World Aquatics ושל איגודים לאומיים. איננו מבקשים את המידע הזה מהשחיינים.

**מהו.** שם השחיין (בעברית, ואם קיים — גם באנגלית), שנת לידה, מועדון ומשחים — זמנים, מקומות, מדליות, ניקוד וזמני ביניים, כפי שהודפסו בפרוטוקול. מתוך התוצאות אנחנו מחשבים נתונים משלנו: שיאים אישיים ועונתיים, רמות, מקום בין בני אותו גיל, השוואות ראש בראש.

**לשם מה.** אנו משתמשים בתוצאות תחרות שפורסמו כדי להציג תיעוד תחרותי הניתן לחיפוש וסיכומים סטטיסטיים של הישגים ספורטיביים.

**דיוק.** אנחנו מציגים את מה שכתוב בפרוטוקול; כשיש בפרוטוקול טעות ברורה, הנתון הרשמי נשאר והשורה מסומנת “Under review”. איננו יכולים לשנות את הפרוטוקול הרשמי של האיגוד. אנו יכולים לתקן טעויות בהעתקה, בזיהוי ובחישובים של SwimHub — למשל תוצאה ששויכה לאדם הלא נכון או שני אנשים שאוחדו לאחד — ונבחן בקשות הנוגעות למידע אישי לא מדויק המוצג באתר שלנו.

## מידע על בעלי חשבונות

כדי לצפות בתוצאות לא צריך חשבון. **מסירת מידע לצורך חשבון היא מרצון — אין חובה חוקית למסור אותו.** בלי חשבון אי אפשר לשמור מועדפים, להצטרף לקבוצות או להוסיף סרטונים; כל השאר עובד.

מה אנחנו שומרים ולשם מה:

- **אימייל, שם תצוגה**, ואם נכנסים עם Google — **תמונת הפרופיל ב-Google** — לניהול החשבון ולשליחת הודעות חשבון (אישור כתובת, איפוס סיסמה);
- **גיבוב (hash) של הסיסמה**, לעולם לא הסיסמה עצמה, אם נכנסים עם אימייל וסיסמה — כדי לאפשר כניסה;
- **האישור שמלאו לכם 13** (ואם טרם מלאו לכם 18 — שיש לכם אישור של הורה או אפוטרופוס) ומתי ניתן — ראו “ילדים”;
- **היסטוריית כניסות** — תאריך, שעה וכתובת IP — כדי להגן על החשבון מפני שימוש לרעה;
- **מה שהוספתם בעצמכם**: מועדפים, קבוצות וחבריהן, אימונים, קישורים לתמונות ולסרטונים, תגובות — כדי להציג אותם לכם ולקהל שבחרתם.

איננו משתמשים במידע שלכם לפרסום, איננו מוכרים אותו ואיננו מעבירים אותו לאיש לצורכי שיווק. איננו מקבלים לגביכם החלטות אוטומטיות.

## מי יכול לראות או לקבל מידע

- **כולם.** מידע תחרותי שפורסם — התוצאות ודפי השחיינים, המועדונים והתחרויות שנבנים מהן — זמין לכל המבקרים. דפי תחרויות, מועדונים ושחיינים בגירים עשויים להיות מאונדקסים במנועי חיפוש; ראו “מנועי חיפוש”.
- **חברי קבוצה.** מידע שמשותף בקבוצה — חבריה, אימונים, מסלולים, מדיה — זמין לחברי אותה קבוצה. שחיינים שמאמן הוסיף לקבוצה גלויים רק בתוך אותה קבוצה.
- **הקהל שבחרתם.** מדיה שהוספתם גלויה ברמה שבחרתם: רק לכם, לחברי קבוצה או לכולם. מדיה שהפכה לציבורית מופיעה בדפים ציבוריים ועשויה להימצא במנועי חיפוש. הגדרות החשיפה ב-SwimHub אינן משנות את הגדרות הפרטיות של תוכן מקושר בשירותים חיצוניים — סרטון YouTube נשאר ציבורי או פרטי כפי שהוא ב-YouTube.
- **רשויות** — רק כשהחוק מחייב זאת.

בנוסף, אנו נעזרים בנותני שירות שמפעילים את האתר עבורנו, כל אחד למשימתו:

- **Microsoft Azure**, אזור Israel Central — אחסון האתר ומסד הנתונים;
- **Resend** — שליחת הודעות חשבון;
- **Google** — “כניסה עם Google”; אנחנו מקבלים רק שם, אימייל ותמונת פרופיל;
- **Cloudflare** — שם הדומיין של האתר והעברת דואר של swimhub.co.il.

חלק מנותני השירות מעבדים מידע אישי מחוץ לישראל, לרבות בארצות הברית. מידע זה עשוי לכלול כתובות אימייל, תוכן הודעות ומידע הקשור להזדהות.

## ילדים

- **פתיחת חשבון מותרת מגיל 13. אם טרם מלאו לכם 18, נדרש אישור של הורה או אפוטרופוס.** מאשרים זאת בהרשמה; איננו מבקשים מסמכים.
- לילדים מתחת לגיל 13 אין חשבון משלהם. הורה יכול לעקוב אחר תוצאות ילדיו מהחשבון שלו.
- **דפי פרופיל של שחיינים מתחת לגיל 18 אינם מאונדקסים במנועי חיפוש** ואינם במפת האתר. שמותיהם ממשיכים להופיע בתוצאות התחרויות והמועדונים, כמו בפרוטוקול הרשמי.
- לפני הוספת מידע אישי או מדיה של שחיין לקבוצה, יש לקבל את הסכמתו, ולגבי קטין — את הסכמת ההורה או האפוטרופוס לשיתוף המתוכנן.

## מנועי חיפוש

דפי תחרויות, מועדונים ושחיינים בגירים עשויים להופיע במנועי חיפוש כמו Google, וכך גם מדיה שבעליה הפך אותה לציבורית. קבוצות והמועדפים שלכם — אף פעם לא; דפי פרופיל של שחיינים מתחת לגיל 18 אינם מאונדקסים.

**כדי להוציא דף פרופיל של שחיין ממנועי החיפוש**, כתבו ל-privacy@swimhub.co.il. נבקש ממנועי החיפוש לא לאנדקס את הדף ונוציא אותו ממפת האתר. הבקשה מתייחסת לדף הפרופיל של השחיין. שמו עשוי להמשיך להופיע בדפי תחרויות ומועדונים. מנועי החיפוש קובעים מתי האינדקס שלהם מתעדכן. נבחן גם בקשות להגבלת הצגה או להסרת מידע אישי, בהתאם לדין ולנסיבות.

## כמה זמן אנחנו שומרים מידע

- תוצאות שחיינים — כל עוד האתר קיים, כחלק מתיעוד התחרות;
- מידע החשבון ומה שהוספתם — עד שתסירו אותו או שהחשבון יימחק;
- היסטוריית כניסות — כ-90 יום;
- יומני שרת טכניים (בקשות ושגיאות, שעשויים לכלול כתובות IP) — לתקופה המוגבלת הנדרשת להפעלת האתר ולהגנה עליו;
- אימיילים שתשלחו אלינו — כל עוד נדרש לטיפול בבקשה;
- גיבויים של מסד הנתונים — 14 יום; מידע שנמחק מהאתר נעלם מהגיבויים בתוך תקופה זו.

## מחיקת חשבון

כתבו ל-privacy@swimhub.co.il מכתובת האימייל של החשבון. נמחק את פרטי החשבון ואת מה שקשור רק אליכם — מועדפים, קישורי המדיה שלכם ותגובות. לגבי קבוצות שאתם מנהלים נסכם איתכם אם להעביר אותן או למחוק. מחיקת חשבון אינה מוחקת תוצאות תחרות שפורסמו.

## הזכויות שלכם

לפי חוק הגנת הפרטיות אפשר לבקש לעיין במידע שאנחנו מחזיקים עליכם, ולבקש לתקן או למחוק מידע שאינו נכון, שלם, ברור או מעודכן. הורה או אפוטרופוס רשאי להגיש בקשות עבור קטין. לפני מסירה או שינוי של מידע ננקוט אמצעים סבירים לוודא מי מבקש, ולגבי הורה — את סמכותו.

כתבו ל-**privacy@swimhub.co.il**. נאפשר עיון במידע בתוך 30 יום, כקבוע בחוק, ונטפל בבקשות אחרות מוקדם ככל שניתן באופן סביר. אם לדעתכם טעינו, אפשר להתלונן בפני הרשות להגנת הפרטיות (gov.il).

## עוגיות והדפדפן שלכם

אנו משתמשים רק בעוגיות שהאתר זקוק להן: אחת שמשאירה אתכם מחוברים, אחת זמנית בזמן “כניסה עם Google”, ועוגיית אבטחה שמגינה על טפסים. הגדרות תצוגה — מצב בהיר או כהה, שפת ההסברים — נשמרות בדפדפן שלכם. אין עוגיות פרסום או מעקב ואין כלי ניתוח של צד שלישי, והודעות החשבון נשלחות ללא מעקב פתיחה או הקלקה. סרטונים מוטמעים משתמשים במצב הפרטיות המוגברת של YouTube; תמונות פרופיל מ-Google נטענות מהשרתים של Google.

## אבטחה

החיבור לאתר מוצפן, סיסמאות נשמרות רק כגיבוב, והגישה למסד הנתונים מוגבלת למפעיל. אין מערכת בטוחה לחלוטין; אם דליפה תסכן את המידע שלכם, נפעל כפי שהחוק מחייב, לרבות הודעה לרשות ולכם, כשהדבר נדרש.

## שינויים

אם המדיניות תשתנה, הגרסה החדשה תופיע בדף הזה עם תאריך חדש ותחול מאותו מועד ואילך. על שינוי מהותי שנוגע לבעלי חשבונות נודיע גם באתר. לא נשתמש במידע שכבר נאסף למטרה חדשה מהותית ללא ההודעה או ההסכמה שהחוק מחייב.
````

---

# Приложение C. Условия пользования — English (`/terms`)

````markdown
# Terms of use

Last updated: 14 October 2026.

SwimHub (swimhub.co.il) is a free site with results of swimming competitions in Israel and swimming records, run by a private individual in Israel. By using the site you agree to these terms. How we handle personal information is described in the [Privacy policy](route:privacy).

## About the site

- Results come from the official results of the Israel Swimming Association and the protocols it publishes, and from the record lists of World Aquatics and national federations. SwimHub is **not** the federation’s official site and does not represent it.
- We show results as printed in the protocol and mark obvious protocol errors “Under review”. Levels, personal and season bests, rankings and comparisons are our own calculations.
- We do our best to keep the site accurate and available, but we don’t guarantee that every figure is correct or that the site is always up. **For anything official — selection, qualification standards, records, appeals — the federation’s results are what counts.**

## Accounts

- You must be at least 13 to create an account. If you are under 18, you must have permission from a parent or legal guardian. Children under 13 don’t have their own account; a parent can follow their results from the parent’s account.
- One account per person; the details you give must be your own.
- Keep your sign-in details safe and don’t share them. If you suspect someone else is using your account, change your password and tell us at privacy@swimhub.co.il.
- You can stop using the site at any time and ask us to delete your account — see the [Privacy policy](route:privacy).

## What you add

Favorites, groups, trainings, links to photos and videos, reactions — you add them yourself and you are responsible for them.

- **Visibility.** You choose who sees media you add: only you, a group’s members, or everyone. Public content is visible to all visitors and may be found by search engines. Visibility settings on SwimHub do not change the privacy settings of linked content on external services.
- **Permission.** Share only what you may share: the photo or video is yours or you have permission, and the people in it agree to it being shared at the visibility you choose — for a minor, their parent or legal guardian.
- **Groups.** Before adding a swimmer’s personal information or media to a group, you must obtain the swimmer’s permission and, for a minor, permission from their parent or legal guardian for the intended sharing.
- **Content rules.** Content must not be offensive, discriminatory, harassing, sexually explicit or violent, must not invade anyone’s privacy, must not advertise or impersonate anyone, and must not break the law.

A link to a video stays on the service where it is hosted (for example YouTube); we show it, we don’t copy it. You keep the rights to what you add and allow us to display it on the site at the visibility you chose.

## What is not allowed

- automated mass downloading of the site or its API, or putting a load on the site that disturbs others;
- trying to get around access limits — reaching closed groups, other people’s accounts or the admin area;
- using information from the site to contact, follow or harass swimmers, especially children.

## Our rights

We may remove content that breaks these terms or that is reported with good reason, limit or close an account that breaks them, and change or stop parts of the site. Anyone can report content with the “Report” button or by writing to info@swimhub.co.il.

## Names and logos

Club names, emblems and federation names belong to their owners and are shown to identify them. If you own a mark and object to how it is shown, write to us.

## Responsibility

The results on the site are not official, and we don’t promise that the site will always be available or error-free. To the extent permitted by law, SwimHub and its operator are not liable for loss caused by relying on the site’s data or by interruptions to the site. Nothing in these terms limits rights you have by law.

## Law and changes

These terms are governed by Israeli law. Jurisdiction is determined by applicable law. If we change the terms, the new version appears on this page with a new date and applies from then on; a significant change we will also announce on the site. Questions — info@swimhub.co.il.
````

---

# Приложение D. Условия пользования — עברית (`/terms`)

````markdown
# תנאי שימוש

עודכן: 14 באוקטובר 2026.

SwimHub (swimhub.co.il) הוא אתר חינמי עם תוצאות של תחרויות שחייה בישראל ושיאי שחייה, המופעל על ידי אדם פרטי בישראל. השימוש באתר מהווה הסכמה לתנאים אלה. האופן שבו אנחנו מטפלים במידע אישי מתואר ב[מדיניות הפרטיות](route:privacy).

## על האתר

- התוצאות לקוחות מהתוצאות הרשמיות של איגוד השחייה בישראל ומהפרוטוקולים שהוא מפרסם, ומרשימות השיאים של World Aquatics ושל איגודים לאומיים. SwimHub **אינו** האתר הרשמי של האיגוד ואינו מייצג אותו.
- התוצאות מוצגות כפי שהודפסו בפרוטוקול, וטעויות ברורות בפרוטוקול מסומנות “Under review”. רמות, שיאים אישיים ועונתיים, דירוגים והשוואות הם חישובים שלנו.
- אנחנו משתדלים שהאתר יהיה מדויק וזמין, אך איננו מתחייבים שכל נתון נכון או שהאתר זמין תמיד. **לכל עניין רשמי — סגל, קריטריונים, שיאים, ערעורים — הקובעות הן תוצאות האיגוד.**

## חשבונות

- פתיחת חשבון מותרת מגיל 13. אם טרם מלאו לכם 18, נדרש אישור של הורה או אפוטרופוס. לילדים מתחת לגיל 13 אין חשבון משלהם; הורה יכול לעקוב אחר תוצאותיהם מהחשבון שלו.
- חשבון אחד לאדם; הפרטים שנמסרים צריכים להיות שלכם.
- שמרו על פרטי הכניסה ואל תשתפו אותם. אם אתם חושדים שמישהו אחר משתמש בחשבון שלכם, החליפו סיסמה והודיעו לנו ב-privacy@swimhub.co.il.
- אפשר להפסיק להשתמש באתר בכל עת ולבקש למחוק את החשבון — ראו [מדיניות הפרטיות](route:privacy).

## מה שאתם מוסיפים

מועדפים, קבוצות, אימונים, קישורים לתמונות ולסרטונים, תגובות — אתם מוסיפים אותם ואחראים להם.

- **חשיפה.** אתם בוחרים מי רואה מדיה שהוספתם: רק אתם, חברי קבוצה או כולם. תוכן ציבורי גלוי לכל המבקרים ועשוי להימצא במנועי חיפוש. הגדרות החשיפה ב-SwimHub אינן משנות את הגדרות הפרטיות של תוכן מקושר בשירותים חיצוניים.
- **רשות.** שתפו רק מה שמותר לכם לשתף: התמונה או הסרטון שלכם או שיש לכם רשות, והאנשים שמופיעים בהם מסכימים לשיתוף ברמת החשיפה שבחרתם — ולגבי קטין, ההורה או האפוטרופוס שלו.
- **קבוצות.** לפני הוספת מידע אישי או מדיה של שחיין לקבוצה, יש לקבל את הסכמתו, ולגבי קטין — את הסכמת ההורה או האפוטרופוס לשיתוף המתוכנן.
- **כללי תוכן.** התוכן לא יהיה פוגעני, מפלה, מטריד, בעל אופי מיני מפורש או אלים, לא יפגע בפרטיות של איש, לא יכלול פרסומת או התחזות ולא יפר את החוק.

קישור לסרטון נשאר בשירות שבו הסרטון מאוחסן (למשל YouTube): אנחנו מציגים אותו ולא מעתיקים. הזכויות במה שהוספתם נשארות שלכם, ואתם מרשים לנו להציג אותו באתר ברמת החשיפה שבחרתם.

## מה אסור

- הורדה המונית אוטומטית של האתר או של ה-API שלו, או עומס על האתר שמפריע לאחרים;
- ניסיון לעקוף הגבלות גישה — להגיע לקבוצות סגורות, לחשבונות של אחרים או לממשק הניהול;
- שימוש במידע מהאתר כדי ליצור קשר עם שחיינים, לעקוב אחריהם או להטריד אותם, ובמיוחד ילדים.

## הזכויות שלנו

אנחנו רשאים להסיר תוכן שמפר תנאים אלה או שדווח עליו בצדק, להגביל או לסגור חשבון מפר, ולשנות או להפסיק חלקים מהאתר. כל אחד יכול לדווח על תוכן בכפתור “Report” או במייל ל-info@swimhub.co.il.

## שמות וסמלים

שמות מועדונים, סמלים ושמות איגודים שייכים לבעליהם ומוצגים כדי לזהות אותם. אם אתם בעלי סימן ומתנגדים לאופן הצגתו — כתבו לנו.

## אחריות

התוצאות באתר אינן רשמיות, ואיננו מתחייבים שהאתר יהיה זמין תמיד או נקי משגיאות. ככל שהדין מתיר, SwimHub ומפעילו אינם אחראים לנזק שנגרם מהסתמכות על נתוני האתר או מהפסקות בפעילותו. דבר בתנאים אלה אינו גורע מזכויות שיש לכם לפי דין.

## דין ושינויים

על תנאים אלה חלים דיני מדינת ישראל. סמכות השיפוט תיקבע בהתאם לדין. אם התנאים ישתנו, הגרסה החדשה תופיע בדף הזה עם תאריך חדש ותחול מאותו מועד ואילך; על שינוי מהותי נודיע גם באתר. שאלות — info@swimhub.co.il.
````
