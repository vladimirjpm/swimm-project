// -*- coding: utf-8 -*-
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using Swimm.Parsing.Models;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.IsrOrg;

public static class IsrOrgCompetitionParser
{
    private static Regex? _headerRxHE;
    private static Regex? _headerRxEN;
    private static Regex? _headerRxHESimple;
    private static Regex? _genderAgeLineRxHE;
    private static Regex? _fullResultRx;
    private static Regex? _relayHeaderRxHE;
    private static Regex? _relayHeaderRxHE2;
    private static Regex? _relayTeamLineRxHE;
    private static Regex? _dateLineRx;

    private const string GenderPatternOriginal =
        "בנות|בנים|נשים|גברים";

    private const string GenderPatternReversed =
        "תונב|םינב|םישנ|םירבג";

    private const string HebrewMix = "מיקס";
    private const string HebrewMixReversed = "סקימ";

    private const string HebrewKlali = "כללי";
    private const string HebrewKlaliReversed = "יללכ";

    private const string GenderPatternWithMix =
        GenderPatternOriginal + "|" + GenderPatternReversed + "|" + HebrewMix + "|" + HebrewMixReversed;

    private static Regex HeaderRxHE => _headerRxHE ??= new Regex(
        @"^(?<len>\d+[Kk]?)\s+(?<style>.+?)\s*-\s*(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex HeaderRxHESimple => _headerRxHESimple ??= new Regex(
        @"^(?<len>\d+[Kk]?)\s+(?<style>[֐-׿\s]+)$",
        RegexOptions.Compiled);

    // Заголовок с ТЕКСТОВОЙ категорией вместо «пол + возраст»: «200 חופשי - שומרי שבת
    // מוקדמות צעירים» (заплывы для соблюдающих субботу — поплыв смешанный, пола в шапке нет).
    // Ни HeaderRxHE (ждёт пол+возраст), ни HeaderRxHESimple (не допускает дефис) такое не
    // берут, и до 2026-08-02 строка молча игнорировалась: событие не менялось, а результаты
    // дописывались в предыдущий заплыв — 200 вольным оказались в эстафете 4X50 комплексом.
    // Работает ФОЛЛБЕКОМ после HeaderRxHE, поэтому обычные «- בנים 13-14» сюда не доходят.
    private static Regex? _headerRxHECategory;
    private static Regex HeaderRxHECategory => _headerRxHECategory ??= new Regex(
        @"^(?<len>\d+[Kk]?)\s+(?<style>[֐-׿\s]+?)\s*-\s*(?<cat>[֐-׿][֐-׿\s""׳'\-]*)$",
        RegexOptions.Compiled);

    // То же для эстафеты: «4X50 מעורב שליחים מיקס - שומרי שבת מוקדמות צעירים».
    private static Regex? _relayHeaderRxHECategory;
    private static Regex RelayHeaderRxHECategory => _relayHeaderRxHECategory ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>[֐-׿\s]+?)\s+" +
        "שליח(?:ים|ות)?\\s*" +
        "(?:" + HebrewMix + "|" + HebrewMixReversed + ")?\\s*" +
        @"-\s*(?<cat>[֐-׿][֐-׿\s""׳'\-]*)$",
        RegexOptions.Compiled);

    private static Regex GenderAgeLineRxHE => _genderAgeLineRxHE ??= new Regex(
        @"^(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex? _mastersAgeLineRxHE;
    private static Regex MastersAgeLineRxHE => _mastersAgeLineRxHE ??= new Regex(
        @"^מאסטרס\s+(?<gender>[א-ת])\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex? _mastersRelayAgeLineRxHE;
    private static Regex MastersRelayAgeLineRxHE => _mastersRelayAgeLineRxHE ??= new Regex(
        @"^מאסטרס\s+שליח(?:ות|ים)?\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex HeaderRxEN => _headerRxEN ??= new Regex(
        @"^(?<len>\d+m?)\s+(?<style>.+?)\s*-\s*(?<gender>female|male|girls|boys|women|men)\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex? _headerRxENinHE;
    // Двуязычные PDF (Маккабиада и пр.): «обвязка» страницы на иврите, поэтому файл
    // разбирается по HE-ветке, но заголовок заплыва — на английском в порядке
    // "50m Freestyle - U17 Girls" (<len> <style> - <ageband> <gender>).
    // Матчим по СЫРОЙ строке (до RTL-реверса), т.к. содержимое латиницей.
    private static Regex HeaderRxENinHE => _headerRxENinHE ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>.+?)\s*-\s*(?<age>U?\d+(?:-\d+)?|Open)\s+(?<gender>Girls|Boys|Women|Men|Female|Male|Mixed)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Английский заголовок заплыва БЕЗ пола/возраста в строке (masters-экспорт
    // Maccabiah ARENA): "400m Freestyle" / "200m Individual Medley". Пол и возраст
    // приходят отдельной ивритской строкой "מאסטרס <пол> <возраст>". Матчим по СЫРОЙ.
    private const string EnStylePattern =
        @"Freestyle|Backstroke|Breaststroke|Butterfly|Individual\s+Medley|Medley";

    private static Regex? _headerEnCategoryInHE;
    // Индивидуальный EN-заголовок, где правая часть — КАТЕГОРИЯ без возраста:
    // "50m Freestyle - Men", "100m Butterfly - Women", "50m Freestyle - Men Para".
    // HeaderRxENinHE такие не берёт — он требует возраст ("U17 Girls"), а у взрослых
    // и Para его в протоколе просто нет. Из-за этого в Маккабиаде терялись 39 событий
    // из 91: их строки прилипали к предыдущему событию и получали чужие стиль,
    // дистанцию и пол (женская сотня баттерфляем уезжала в "100m Butterfly - U17 Boys",
    // а Para-заплывы — в "4X100m Medley Relay - Men").
    // Работает как ФОЛЛБЕК после HeaderRxENinHE, чтобы не менять разбор заголовков
    // с возрастом. Стиль ограничен EnStylePattern — правая часть тут «любая», и без
    // этого regex начал бы хватать обычные строки таблицы.
    private static Regex HeaderEnCategoryInHE => _headerEnCategoryInHE ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s*-\s*(?<cat>[A-Za-z][A-Za-z0-9\s-]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex? _headerEnNoGenderInHE;
    private static Regex HeaderEnNoGenderInHE => _headerEnNoGenderInHE ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Английский заголовок ЭСТАФЕТЫ без пола/возраста: "4X50m Medley Relay".
    private static Regex? _relayHeaderEnInHE;
    private static Regex RelayHeaderEnInHE => _relayHeaderEnInHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s+Relay$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // После маркера срыва (DQ/DNS/NS) допускается заметка правила ("/ SW 7.1"):
    // иначе такая строка не считалась полной, подклеивала следующую и теряла её.
    private static Regex FullResultRx => _fullResultRx ??= new Regex(
        @"^(-|\d+)\s+\d+\s+\d+.*((?:\d{1,2}:)?\d{2}:\d{2}\.\d{2}|DNS|DNF|NS|DQ)(\s+(?:/|SW|\d+\.\d+))*\s+\d+$",
        RegexOptions.Compiled);

    // EN-строка команды эстафеты: "<heat> <lane> <team> <time> Rank <pos>".
    // Команда бывает пустой (перенос названия на соседние строки) → опциональна.
    private static Regex? _relayTeamLineEn;
    private static Regex RelayTeamLineEn => _relayTeamLineEn ??= new Regex(
        @"^(?<heat>\d+)\s+(?<lane>\d+)\s+(?:(?<team>.+?)\s+)?(?<time>(?:\d{1,2}:)?\d{2}:\d{2}\.\d{1,2}|DQ|NS)\s+Rank\s+(?<pos>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Каноничное имя английского стиля → к нижнему регистру + individual_medley.
    private static string CanonEnStyle(string style)
    {
        var s = Regex.Replace(style.Trim(), @"\s+", " ").ToLowerInvariant();
        return s switch
        {
            "individual medley" => "individual_medley",
            "medley" => "individual_medley",
            _ => s
        };
    }

    // Чистый EN-экспорт (Maccabiah 2026, файл _IL_EN): заголовок заплыва —
    // "<len>m <style> - <категория>", категория варьируется: "U17 Girls",
    // "Women", "Men Para", а у эстафет "<legs>X<len>m <style> Relay|Mix - MIX 18-99".
    private static Regex? _headerEnFull;
    private static Regex HeaderEnFull => _headerEnFull ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s*-\s*(?<cat>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex? _relayHeaderEnFull;
    private static Regex RelayHeaderEnFull => _relayHeaderEnFull ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s+(?:Relay|Mix)\s*-\s*(?<cat>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Разбор правой части EN-заголовка ("U17 Girls" / "Women" / "MIX 18-99" /
    // "Men Para") в нормализованные пол и возраст. Para/open без числа → возраст
    // "para"/"open" (чтобы Men и Men Para не схлопнулись в одно событие).
    /// <summary>
    /// Текстовая категория ивритского заголовка → стабильный ключ для EventCategory.
    /// Сейчас известна одна — «שומרי שבת» (заплывы для соблюдающих субботу). Хвост вроде
    /// «מוקדמות צעירים» — это название соревнования, в ключ его тащить незачем.
    /// Неизвестная категория → null: событие всё равно создаётся (стиль, дистанция и дата
    /// разбираются верно), просто без пометки программы.
    /// </summary>
    /// <summary>
    /// Похожа ли (уже нормализованная) строка на заголовок нового заплыва — любой из
    /// известных форм. Нужна там, где разбор эстафеты сканирует строки вперёд: без этой
    /// проверки эстафета с недобранным составом съедает следующее событие целиком.
    /// </summary>
    private static bool IsAnyEventHeaderHE(string line) =>
        RelayHeaderRxHE.IsMatch(line) || RelayHeaderRxHE2.IsMatch(line)
        || RelayHeaderRxHECategory.IsMatch(line)
        || RelayHeaderNoCategoryRxHE.IsMatch(line)
        || HeaderRxHE.IsMatch(line) || HeaderRxHECategory.IsMatch(line)
        || HeaderRxHESimple.IsMatch(line);

    private static string? HeCategoryToken(string cat) =>
        cat.Contains("שומרי שבת", StringComparison.Ordinal) ? "shabbat" : null;

    private static (string gender, string age) ParseEnCategory(string cat)
    {
        cat = Regex.Replace(cat.Trim(), @"\s+", " ");
        bool para = Regex.IsMatch(cat, @"\bPara\b", RegexOptions.IgnoreCase);

        string gender = "none";
        string? age = null;
        foreach (var t in cat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (t.ToLowerInvariant())
            {
                case "girls" or "women" or "female": gender = "female"; break;
                case "boys" or "men" or "male": gender = "male"; break;
                case "mix" or "mixed": gender = "none"; break;
                default:
                    if (Regex.IsMatch(t, @"^U?\d+(-\d+)?$", RegexOptions.IgnoreCase))
                        age = t.TrimStart('U', 'u');
                    break;
            }
        }

        return (gender, age ?? (para ? "para" : "open"));
    }

    private static Regex RelayHeaderRxHE => _relayHeaderRxHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+" +
        "שליח(?:ים|ות)?\\s*" +
        "(?:" + HebrewMix + "|" + HebrewMixReversed + ")?\\s*" +
        @"-\s*(?<gender>" +
        "נ|ז|" + GenderPatternWithMix +
        @")\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex RelayHeaderRxHE2 => _relayHeaderRxHE2 ??= new Regex(
        @"^(?<len>\d+)\s*[Xx]\s*(?<legs>\d+)\s+(?<style>.+?)\s+" +
        "שליח(?:ים|ות)?\\s*" +
        "(?:" + HebrewMix + "|" + HebrewMixReversed + ")?\\s*" +
        @"-\s*(?<gender>" +
        "נ|ז|" + GenderPatternWithMix +
        @")\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex RelayTeamLineRxHE => _relayTeamLineRxHE ??= new Regex(
        @"^(?<heat>\d+)\s+(?<lane>\d+)\s+(?<team>.+?)\s+(?<time>(?:\d{1,2}:)?\d{2}:\d{2}\.\d{1,2}|DQ|NS|DNF|DNS)\s+" +
        "מיקום" +
        @"\s+(?<pos>\d+)\s*$",
        RegexOptions.Compiled);

    // Заголовок эстафеты вовсе без категории («4X50 חופשי שליחים») — masters-экспорты ARENA
    // (категория придёт строкой «מאסטרס …» ниже) и Маккаби (категории не будет совсем,
    // событие материализует первая командная строка).
    private static Regex? _relayHeaderNoCategoryRxHE;
    private static Regex RelayHeaderNoCategoryRxHE => _relayHeaderNoCategoryRxHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+שליח(?:ים|ות)?\s*$",
        RegexOptions.Compiled);

    private static Regex DateLineRx => _dateLineRx ??= new Regex(
        @"(?<date>\d{2}/\d{2}/\d{4})$",
        RegexOptions.Compiled);

    private const string HebrewRelay = "שליח";

    private static List<string> _debugLog = new();

    public static string GetDebugLog()
    {
        return string.Join("\n", _debugLog);
    }

    public static IEnumerable<IsrOrgCompetitionResult> ParseCompetitions(Stream pdfStream, string language)
    {
        var results = new List<IsrOrgCompetitionResult>();
        _debugLog.Clear();

        try
        {
            foreach (var result in ParseCompetitionsInternal(pdfStream, language))
            {
                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            var debugInfo = string.Join("\n", _debugLog.TakeLast(50));
            throw new InvalidOperationException(
                $"Error in ParseCompetitions (language={language}): {ex.Message}\n\n--- DEBUG LOG (last 50 lines) ---\n{debugInfo}", ex);
        }

        if (results.Count == 0)
        {
            var debugInfo = string.Join("\n", _debugLog);
            throw new InvalidOperationException(
                $"No competitions found in PDF (language={language}).\n\n--- DEBUG LOG ---\n{debugInfo}");
        }

        return results;
    }

    private static void Log(string message)
    {
        _debugLog.Add($"[{_debugLog.Count + 1}] {message}");
    }

    /// <summary>
    /// Дистанция заголовка → метры. Открытая вода печатается в километрах («5K חופשי»,
    /// «10K חופשי»), бассейн — в метрах («1600 חופשי», «50m Freestyle»). Без перевода
    /// 10 км и 5 км не были бы заголовками вообще: регулярки ждали цифры, строка не
    /// распознавалась, и результаты дописывались в ПРЕДЫДУЩИЙ заплыв — на чемпионате
    /// в Эйлате 2026 так слиплись 3000 нокаут, 5K и 10K, а проверка качества потом
    /// честно ругалась на «повтор дисциплины за день» с временами от 5 минут до двух часов.
    /// </summary>
    internal static string NormalizeHeaderLen(string rawLen)
    {
        var len = rawLen.Trim();
        if (len.EndsWith("m", StringComparison.OrdinalIgnoreCase)) len = len[..^1];
        if (len.EndsWith("k", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(len[..^1], out var km))
            return (km * 1000).ToString();
        return len;
    }

    private static string NormalizeIfHe(string s, bool isHE) =>
        isHE ? HebrewTextHelper.NormalizeHebrewLine(s) : s;

    // Строка-фрагмент перенесённой фамилии: одно "слово" из букв (латиница/иврит),
    // без цифр и пробелов — т.е. не заголовок, не строка результата, не дата.
    private static bool IsNameFragment(string s)
    {
        s = s.Trim();
        return s.Length > 0 && Regex.IsMatch(s, @"^[\p{L}][\p{L}'\-]*$");
    }

    private static IEnumerable<IsrOrgCompetitionResult> ParseCompetitionsInternal(Stream pdfStream, string language)
    {
        Log($"Starting parse, language={language}");

        using var doc = PdfDocument.Open(pdfStream);
        Log($"PDF opened, pages={doc.NumberOfPages}");

        // Извлекаем текст страниц в строки (RTL-порядок восстанавливается позже, при нормализации).
        // Вынесено отдельно, чтобы ядро разбора (ParseLines) можно было тестировать на голых строках.
        bool isHE = language.Equals("HE", StringComparison.OrdinalIgnoreCase);
        var pages = new List<IReadOnlyList<string>>();
        // Колонки Last/First, перенесённые с предыдущей relay-страницы — если её
        // командная таблица разорвана page break'ом (см. ReconstructEnRelaySwimmerNames).
        // Перенос действует только на ОДИН следующий page hop, затем сбрасывается —
        // подтверждённый в PDF кейс это ровно один разрыв посреди 4-ногой команды.
        double? carriedLastColX = null;
        double? carriedFirstColX = null;
        // Роль-вокабуляр колонок (EN "Last"/"First" vs HE "יטרפ"/"החפשמ") тоже
        // переносится через page break вместе с X-координатами — иначе на
        // continuation-странице без собственной шапки мы бы не знали, в каком
        // порядке эмитить восстановленную строку для нижестоящего парсера
        // (EN-потребитель читает "Last First Year" как есть; HE-потребитель
        // прогоняет строку через NormalizeHebrewLine, которая переворачивает
        // порядок токенов, поэтому туда нужно эмитить "Year First Last").
        bool? carriedColsAreHebrew = null;
        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords();
            var groups = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0) * 2.0)
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left)
                    .Select(w => new PositionedWord(w.Text, w.BoundingBox.Left))
                    .ToList())
                .ToList();

            var lines = groups
                .Select(g => string.Join(' ', g.Select(w => w.Text)))
                .ToList();

            // EN-экспорт Маккабиады (в т.ч. интернациональная эстафета внутри
            // HE-экспорта, где таблица команд остаётся англоязычной): имена
            // пловцов в таблице эстафеты визуально переносятся посреди слова
            // (узкие колонки Last/First name), и обрывок попадает на СОСЕДНЮЮ
            // строку (по Y), а не в ту же — join выше это не лечит.
            // Реконструируем такие имена по X-координатам колонок Last/First.
            // Работает независимо от isHE: срабатывает только если в группах
            // реально нашлись заголовки колонок "Last"/"First" — но в HE-экспорте
            // те же английские подписи колонок встречаются и в шапке ОБЫЧНОЙ
            // (не-эстафетной) таблицы результатов, поэтому там реконструкция
            // ошибочно склеивала/удаляла строки индивидуальных заплывов.
            // Ограничиваем запуск страницами, где реально есть командная строка
            // эстафеты "heat lane team time Rank pos" — маркер "Rank <pos>" в
            // конце строки уникален для интернациональной эстафеты Маккабиады
            // и не встречается в обычных индивидуальных результатах. Проверяем
            // и в EN-, и в HE-режиме (условие безвредно для чисто EN-файлов,
            // где реконструкция и так нужна только на эстафетных страницах).
            // HE-native релейная страница (та же Маккабиада, ивритский экспорт того же
            // протокола): командная строка помечена не "Rank N" (латиница), а ивритским
            // столбцом "מיקום" — на СЫРОЙ странице (до нормализации/реверса) это слово
            // приходит уже как отдельный "перевёрнутый" глиф-токен "םוקימ" (см.
            // RelayTeamLineRxHE, где нормализованная форма — "מיקום"). Формат строки —
            // "<pos> םוקימ <time> <team> <heat> <lane>", поэтому маркер ищем сразу после
            // ведущего числа.
            bool looksLikeRelayPage = lines.Any(l => Regex.IsMatch(l, @"\bRank\s+\d+\s*$"))
                || lines.Any(l => Regex.IsMatch(l, @"^\d+\s+םוקימ\b"));

            // Если это НЕ явная relay-страница (нет маркера "Rank N"), но с предыдущей
            // страницы перенесены колонки Last/First — это может быть продолжение
            // командной таблицы, разорванной page break'ом (нет ни командной строки,
            // ни повторной шапки колонок). Пробуем реконструкцию и там.
            bool isCarriedContinuation = !looksLikeRelayPage && carriedLastColX is not null && carriedFirstColX is not null;

            double? seedLast = looksLikeRelayPage ? carriedLastColX : (isCarriedContinuation ? carriedLastColX : null);
            double? seedFirst = looksLikeRelayPage ? carriedFirstColX : (isCarriedContinuation ? carriedFirstColX : null);
            bool? seedIsHebrewVocab = looksLikeRelayPage ? carriedColsAreHebrew : (isCarriedContinuation ? carriedColsAreHebrew : null);

            // Сбрасываем перенос сразу — действует только на один hop вперёд.
            carriedLastColX = null;
            carriedFirstColX = null;
            carriedColsAreHebrew = null;

            if (looksLikeRelayPage || isCarriedContinuation)
            {
                lines = ReconstructEnRelaySwimmerNames(
                    groups, lines, seedLast, seedFirst, seedIsHebrewVocab,
                    out var outLastColX, out var outFirstColX, out var outIsHebrewVocab);

                // Переносим дальше только если ЭТА страница сама была явной relay-страницей
                // (обнаружен маркер "Rank N"/"מיקום") — продолжение продолжения не
                // подтверждено данными.
                if (looksLikeRelayPage)
                {
                    carriedLastColX = outLastColX;
                    carriedFirstColX = outFirstColX;
                    carriedColsAreHebrew = outIsHebrewVocab;
                }
            }

            pages.Add(lines);
        }

        return ParseLines(pages, language);
    }

    // Слово + его X-координата (левый край) на странице. Лёгкая обёртка над
    // UglyToad.PdfPig.Content.Word, чтобы логику реконструкции можно было
    // тестировать без реального PDF/PdfPig-объектов — см.
    // Swimm.Tests/IsrOrgCompetitionParserRelayNameReconstructionTests.cs.
    internal readonly record struct PositionedWord(string Text, double Left);

    /// <summary>
    /// Чинит перенос имён/фамилий эстафетной таблицы EN-экспорта (Maccabiah).
    /// Колонки "Last name"/"First name" узкие, поэтому длинные имена переносятся
    /// на отдельную Y-строку (только обрывок, без остального содержимого строки).
    /// Опорные X-координаты колонок берём из заголовка таблицы ("Last ... First ...",
    /// повторяется перед каждой командой), затем классифицируем строку с годом
    /// рождения (4 цифры) как "опорную" и достраиваем недостающую фамилию/имя
    /// обрывками с соседних строк, если те по X ближе к недостающей колонке.
    /// Консервативно: при любой неоднозначности строки не трогаем (возврат как есть).
    /// </summary>
    internal static List<string> ReconstructEnRelaySwimmerNames(List<List<PositionedWord>> groups, List<string> lines) =>
        ReconstructEnRelaySwimmerNames(groups, lines, null, null, null, out _, out _, out _);

    /// <summary>
    /// Обратная совместимость для существующих юнит-тестов (LTR-only, без вокабуляра ролей).
    /// </summary>
    internal static List<string> ReconstructEnRelaySwimmerNames(
        List<List<PositionedWord>> groups, List<string> lines,
        double? seedLastColX, double? seedFirstColX,
        out double? finalLastColX, out double? finalFirstColX) =>
        // Вызывающие этот overload (старые EN-only юнит-тесты и продакшн-код до
        // добавления HE-вокабуляра) всегда имели дело только с EN-колонками — если
        // сид передан БЕЗ явного вокабуляра, но X-координаты колонок всё же заданы,
        // это заведомо EN-контекст (единственный, что существовал раньше).
        ReconstructEnRelaySwimmerNames(groups, lines, seedLastColX, seedFirstColX,
            seedLastColX is not null || seedFirstColX is not null ? false : null,
            out finalLastColX, out finalFirstColX, out _);

    /// <summary>
    /// Перегрузка с "затравочными" X-координатами колонок Last/First — используется,
    /// когда команда эстафеты разорвана page break'ом: продолжение (2 последние ноги)
    /// печатается в начале СЛЕДУЮЩЕЙ страницы БЕЗ повторной шапки "Last First ...".
    /// Вызывающий код (ParseCompetitionsInternal) переносит колонки с предыдущей
    /// relay-страницы через seedLastColX/seedFirstColX, а finalLastColX/finalFirstColX
    /// возвращает наружу, если в ЭТОЙ странице шапка встретилась (для следующей пары).
    /// </summary>
    internal static List<string> ReconstructEnRelaySwimmerNames(
        List<List<PositionedWord>> groups, List<string> lines,
        double? seedLastColX, double? seedFirstColX, bool? seedIsHebrewVocab,
        out double? finalLastColX, out double? finalFirstColX, out bool? finalIsHebrewVocab)
    {
        var result = new List<string>(lines);
        var consumed = new HashSet<int>();
        var replacements = new Dictionary<int, string>();

        double? lastColX = seedLastColX;
        double? firstColX = seedFirstColX;
        // Вокабуляр шапки, из которого узнали роли колонок: EN ("Last"/"First") или
        // HE ("יטרפ"/"החפשמ"). От него зависит, в каком порядке эмитить восстановленную
        // строку (см. использование ниже) — потребители различаются: EN-ветка читает
        // "Last First Year" как есть (сырую строку), HE-ветка (RelayTeamLineRxHE +
        // цикл сборки состава) прогоняет строку через NormalizeHebrewLine, которая
        // ЦЕЛИКОМ переворачивает порядок токенов, поэтому туда нужно отдавать
        // "Year First Last" — после переворота получится нужный порядок.
        bool? isHebrewVocab = seedIsHebrewVocab;

        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];

            var lastHeaderIdx = g.FindIndex(w => w.Text == "Last");
            var firstHeaderIdx = g.FindIndex(w => w.Text == "First");
            if (lastHeaderIdx >= 0 && firstHeaderIdx >= 0)
            {
                lastColX = g[lastHeaderIdx].Left;
                firstColX = g[firstHeaderIdx].Left;
                isHebrewVocab = false;
                continue;
            }

            // Ивритская шапка того же протокола (Maccabiah HE-экспорт): колонки те же
            // "Last name"/"First name" по смыслу, но подписаны на иврите и с обратным
            // (RTL) физическим порядком — год слева, имя посередине, фамилия справа
            // (см. диагностику реального PDF: заголовки на отдельных строках блока
            // сплит-таймов — "...משפחה שם" (перевёрнуто как "םש"+"החפשמ") и
            // "...פרטי שם" (как "יטרפ"+"םש")). Токены "החפשמ" ("family"→Last name) и
            // "יטרפ" ("private"→First name) уникальны каждый своей роли (в отличие от
            // "םש"="שם"="name", который встречается в ОБЕИХ парах и потому неоднозначен
            // как якорь), поэтому классифицируем колонку по ним напрямую — X-координата
            // РОЛИ берётся как есть, без предположений о её стороне (лево/право).
            var heLastIdx = g.FindIndex(w => w.Text == "החפשמ"); // החפשמ
            if (heLastIdx >= 0)
            {
                lastColX = g[heLastIdx].Left;
                isHebrewVocab = true;
                continue;
            }

            var heFirstIdx = g.FindIndex(w => w.Text == "יטרפ"); // יטרפ
            if (heFirstIdx >= 0)
            {
                firstColX = g[heFirstIdx].Left;
                isHebrewVocab = true;
                continue;
            }

            if (lastColX is null || firstColX is null) continue;
            // Роль-вокабуляр не установлен (не должно происходить, если lastColX/firstColX
            // не null — они выставляются только вместе с isHebrewVocab), но проверяем явно:
            // без известного вокабуляра эмитить строку безопасно НЕЛЬЗЯ (не знаем, в каком
            // порядке — не гадаем, пропускаем строку как есть).
            if (isHebrewVocab is null) continue;
            if (consumed.Contains(i)) continue;

            var yearIdx = g.FindIndex(w => Regex.IsMatch(w.Text, @"^\d{4}$"));
            if (yearIdx < 0) continue;
            var yearWord = g[yearIdx];

            // Строка ноги эстафеты содержит год + не больше 2 доп. слов (имя, фамилия).
            // ОБЫЧНАЯ (не-эстафетная) строка результата, случайно содержащая год рождения
            // (он там тоже есть), несёт МНОГО дополнительных токенов — ранг, время, клуб,
            // сплит-таймы, очки (реально 6-9 слов). Не бракуем эту консервативную защиту:
            // без неё carry-continuation, переносящий колонки на следующую HE-страницу,
            // способен принять целую страницу ОБЫЧНЫХ результатов за хвост эстафетной
            // таблицы (нет собственной шапки, но каждая строка содержит год) и подменить
            // информативные строки урезанным "фейковым именем" — воспроизведено при
            // диагностике (851→808 индивидуальных строк).
            if (g.Count - 1 > 2) continue;

            var otherWords = g.Where((w, idx) => idx != yearIdx).ToList();
            // Опорная строка результата эстафеты ("1 4 Israel 04:05.04 Rank 1") тоже может
            // случайно содержать 4-значный токен — но там больше 3 колоночных слов и они
            // не близки к Last/First колонкам. Такие строки просто не наберут last/first ниже.
            string? lastCore = ClassifyNearest(otherWords, lastColX.Value, firstColX.Value, wantLast: true);
            string? firstCore = ClassifyNearest(otherWords, lastColX.Value, firstColX.Value, wantLast: false);

            bool lastMissing = lastCore is null;
            bool firstMissing = firstCore is null;

            // Раньше здесь был ранний continue, если ОБЕ колонки не распознались рядом
            // с годом (типичный случай — шапка таблицы/посторонний текст). Но при
            // двойном переносе (и Last, и First сломаны переносом ОДНОВРЕМЕННО) год
            // печатается совсем ОДИН на своей Y-строке — обе колонки целиком уезжают
            // на соседние строки (Last+First-обрывки ДО года, их суффиксы ПОСЛЕ), и
            // otherWords пуст. Поэтому здесь больше не бракуем такую строку сразу —
            // ниже пытаемся достроить обе колонки из соседних fragment-групп, и уже
            // финальная проверка (last.Length==0 || first.Length==0) отбраковывает
            // некандидатов: шапки/посторонний текст не наберут распознанных обрывков
            // в соседних Y-строках по X-координате колонок.
            string lastPrefix = "", lastSuffix = "", firstPrefix = "", firstSuffix = "";

            if (lastMissing || firstMissing)
            {
                if (i > 0 && !consumed.Contains(i - 1) &&
                    TryFillFromFragmentGroup(groups[i - 1], lastColX.Value, firstColX.Value, lastMissing, firstMissing,
                        out var lp, out var fp))
                {
                    lastPrefix = lp;
                    firstPrefix = fp;
                    consumed.Add(i - 1);
                }

                // Суффикс проверяем по ИСХОДНЫМ lastMissing/firstMissing (не по тому,
                // закрыл ли уже что-то prefix): один и тот же перенесённый столбец может
                // быть разорван НА ТРИ строки вокруг года — префикс ДО ("DABBA"), сам год
                // строкой "Alan 2008" (без Last вообще), и суффикс ПОСЛЕ ("H") —
                // last = DABBA + "" + H = DABBAH. Здесь core уже null, поэтому оба
                // обрывка нужны одновременно для ОДНОЙ и той же колонки, а не только
                // "то, что не заполнил prefix".
                if (i + 1 < groups.Count && !consumed.Contains(i + 1) &&
                    TryFillFromFragmentGroup(groups[i + 1], lastColX.Value, firstColX.Value, lastMissing, firstMissing,
                        out var ls, out var fs))
                {
                    lastSuffix = ls;
                    firstSuffix = fs;
                    consumed.Add(i + 1);
                }
            }

            string last = (lastPrefix + (lastCore ?? "") + lastSuffix).Trim();
            string first = (firstPrefix + (firstCore ?? "") + firstSuffix).Trim();

            if (last.Length == 0 || first.Length == 0) continue;

            // Порядок эмита зависит от того, чей вокабуляр шапки распознан (см. комментарий
            // у объявления isHebrewVocab выше). EN-потребитель (RelayTeamLineEn-ветка) читает
            // строку как есть — эмитим готовый порядок "Last First Year". HE-потребитель
            // (RelayTeamLineRxHE-ветка) сначала прогоняет строку через NormalizeHebrewLine,
            // которая переворачивает ВЕСЬ порядок токенов — поэтому туда эмитим ЗЕРКАЛЬНЫЙ
            // порядок "Year First Last", чтобы после переворота получить "Last First Year".
            replacements[i] = isHebrewVocab == true
                ? $"{yearWord.Text} {first} {last}"
                : $"{last} {first} {yearWord.Text}";
        }

        finalLastColX = lastColX;
        finalFirstColX = firstColX;
        finalIsHebrewVocab = isHebrewVocab;

        if (replacements.Count == 0 && consumed.Count == 0) return result;

        var final = new List<string>();
        for (int i = 0; i < result.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            final.Add(replacements.TryGetValue(i, out var repl) ? repl : result[i]);
        }

        return final;
    }

    /// <summary>
    /// Пытается объяснить ВСЮ соседнюю Y-группу (1 или 2 слова) как обрывок(и)
    /// недостающих колонок Last/First. Раньше признавали только группы из ровно
    /// одного слова (перенос ломал одну колонку за раз) — но перенос может сломать
    /// ОБЕ колонки одновременно, и тогда оба обрывка попадают на одну соседнюю
    /// строку как два отдельных "слова" (напр. "VSKY n" — Last-суффикс и
    /// First-суффикс одной группой). Строго консервативно: группа принимается,
    /// только если КАЖДОЕ слово однозначно попадает в РЕАЛЬНО недостающую колонку
    /// (никаких лишних/конфликтующих/неоднозначных слов) — иначе вся группа
    /// отклоняется и строка остаётся как есть.
    /// </summary>
    private static bool TryFillFromFragmentGroup(
        List<PositionedWord> group, double lastColX, double firstColX,
        bool lastMissing, bool firstMissing,
        out string lastFrag, out string firstFrag)
    {
        lastFrag = "";
        firstFrag = "";

        if (group.Count == 0 || group.Count > 2) return false;
        if (!group.All(w => IsNameFragment(w.Text))) return false;
        if (!lastMissing && !firstMissing) return false;

        string? last = null, first = null;
        foreach (var w in group)
        {
            var col = NearestColumn(w.Left, lastColX, firstColX);
            if (col == "last" && lastMissing && last is null) last = w.Text;
            else if (col == "first" && firstMissing && first is null) first = w.Text;
            else return false; // непристыкованное/неоднозначное/дублирующее слово — бракуем всю группу
        }

        // Двухсловная группа обязана объяснить оба слова недостающими колонками —
        // иначе один из "фрагментов" на самом деле посторонний текст.
        if (group.Count == 2 && (last is null || first is null)) return false;

        lastFrag = last ?? "";
        firstFrag = first ?? "";
        return true;
    }

    private static string NearestColumn(double x, double lastColX, double firstColX)
    {
        double dLast = Math.Abs(x - lastColX);
        double dFirst = Math.Abs(x - firstColX);
        // Неоднозначная классификация (координаты слишком близки) — не рискуем.
        if (Math.Abs(dLast - dFirst) < 3.0) return "ambiguous";
        return dLast < dFirst ? "last" : "first";
    }

    private static string? ClassifyNearest(List<PositionedWord> otherWords, double lastColX, double firstColX, bool wantLast)
    {
        PositionedWord? best = null;
        double bestDist = double.MaxValue;
        foreach (var w in otherWords)
        {
            var col = NearestColumn(w.Left, lastColX, firstColX);
            if (col != (wantLast ? "last" : "first")) continue;
            var dist = Math.Abs(w.Left - (wantLast ? lastColX : firstColX));
            if (dist < bestDist) { bestDist = dist; best = w; }
        }
        return best?.Text;
    }

    /// <summary>
    /// Ядро разбора: принимает уже извлечённые из PDF строки (по странице на список).
    /// Тестируется напрямую на строках — см. Swimm.Tests/IsrOrgCompetitionParserTests.cs.
    /// </summary>
    internal static IEnumerable<IsrOrgCompetitionResult> ParseLines(
        IReadOnlyList<IReadOnlyList<string>> pages, string language)
    {
        bool isHE = language.Equals("HE", StringComparison.OrdinalIgnoreCase);
        var headerRx = isHE ? HeaderRxHE : HeaderRxEN;

        Log($"ParseLines start, language={language}, isHE={isHE}, pages={pages.Count}");

        IsrOrgCompetitionResult? current = null;

        bool currentIsRelay = false;
        int currentRelayLegs = 0;
        string dat_relay = "";

        string? pendingEventLen = null;
        string? pendingEventStyle = null;
        string? pendingEventLine = null;

        string? pendingRelayStyleHe = null;
        string? pendingRelayLen = null;
        int pendingRelayLegs = 0;

        IsrOrgResult? pendingRelayResult = null;
        List<RelaySwimmer>? pendingSwimmers = null;
        int pendingSwimmersOrder = 1;

        // Кол-во строк в начале СЛЕДУЮЩЕЙ страницы, уже "съеденных" как ноги
        // эстафеты, разорванной page-break'ом (см. ветку EN relay ниже) — их
        // не нужно заново прогонять через основной цикл разбора строк.
        int skipLeadingLinesNextPage = 0;

        for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
        {
            int pageNumber = pageIdx + 1;
            var lines = pages[pageIdx];

            int startI = 0;
            if (skipLeadingLinesNextPage > 0)
            {
                startI = Math.Min(skipLeadingLinesNextPage, lines.Count);
                skipLeadingLinesNextPage = 0;
            }

            Log($"--- Page {pageNumber} ---");
            Log($"Page {pageNumber}: {lines.Count} lines extracted");

            for (int i = startI; i < lines.Count; i++)
            {
                var raw = lines[i].Trim();
                var line = isHE ? HebrewTextHelper.NormalizeHebrewLine(raw) : raw;

                Log($"L{i}: raw='{raw.Substring(0, Math.Min(60, raw.Length))}...' norm='{line.Substring(0, Math.Min(60, line.Length))}...'");

                if (isHE && pendingRelayLen != null)
                {
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);
                    var mastersRelayAgeMatch = MastersRelayAgeLineRxHE.Match(line);

                    if (mastersAgeMatch.Success || mastersRelayAgeMatch.Success)
                    {
                        string ageGroup;
                        string genderNorm;

                        if (mastersAgeMatch.Success)
                        {
                            ageGroup = mastersAgeMatch.Groups["age"].Value;
                            var genderRaw = mastersAgeMatch.Groups["gender"].Value;
                            genderNorm = HebrewTextHelper.NormalizeGenderHE(genderRaw.Trim());
                            Log($"  -> MATCH MastersAge for pending relay: gender={genderRaw}, age={ageGroup}");
                        }
                        else
                        {
                            ageGroup = mastersRelayAgeMatch.Groups["age"].Value;
                            genderNorm = "none";
                            Log($"  -> MATCH MastersRelayAge for pending relay: age={ageGroup}, gender=none (mixed)");
                        }

                        if (current != null) yield return current;

                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(pendingRelayStyleHe!, pendingRelayStyleHe!);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);

                        currentIsRelay = true;
                        currentRelayLegs = pendingRelayLegs;

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroup,
                            Date: dat_relay,
                            Event: pendingEventLine ?? string.Empty,
                            EventStyleName: styleNorm,
                            EventStyleLen: pendingRelayLen,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroup,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW RELAY EVENT (masters continuation): {current.Event}, gender={genderNorm}");

                        pendingRelayStyleHe = null;
                        pendingRelayLen = null;
                        pendingRelayLegs = 0;
                        pendingEventLine = null;
                        continue;
                    }
                }

                if (pendingRelayResult != null && pendingSwimmers != null && current != null)
                {
                    bool isNewHeader = headerRx.IsMatch(line) || (isHE && IsAnyEventHeaderHE(line));
                    bool isNewTeam = RelayTeamLineRxHE.IsMatch(line);

                    if (!isNewHeader && !isNewTeam && pendingSwimmers.Count < currentRelayLegs)
                    {
                        if (Regex.IsMatch(line, @"\b\d{4}\b"))
                        {
                            pendingSwimmers.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(line, pendingSwimmersOrder));
                            pendingSwimmersOrder++;

                            if (pendingSwimmers.Count >= currentRelayLegs)
                            {
                                current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
                                pendingRelayResult = null;
                                pendingSwimmers = null;
                            }
                            continue;
                        }
                    }
                    else if (isNewHeader || isNewTeam)
                    {
                        if (pendingSwimmers.Count > 0)
                        {
                            current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
                        }
                        pendingRelayResult = null;
                        pendingSwimmers = null;
                    }
                }

                if (isHE && pendingEventLen != null)
                {
                    Log($"  -> Checking for gender/age (pending: len={pendingEventLen}, style={pendingEventStyle})");
                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);
                    if (genderAgeMatch.Success || mastersAgeMatch.Success)
                    {
                        var ageGroupVal = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["age"].Value
                            : mastersAgeMatch.Groups["age"].Value;
                        var genderRaw = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["gender"].Value
                            : mastersAgeMatch.Groups["gender"].Value;

                        Log($"  -> MATCH GenderAge: gender={genderRaw}, age={ageGroupVal}");

                        if (current != null)
                        {
                            Log($"  -> Yielding previous event: {current.Event}");
                            yield return current;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(genderRaw.Trim());
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(pendingEventStyle!, pendingEventStyle!);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroupVal,
                            Date: dat_relay,
                            Event: $"{pendingEventLen} {pendingEventStyle} - {genderRaw} {ageGroupVal}",
                            EventStyleName: styleNorm,
                            EventStyleLen: pendingEventLen,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroupVal,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW EVENT (Format2): {current.Event}, gender={genderNorm}");
                        currentIsRelay = false;
                        currentRelayLegs = 0;
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        pendingEventLine = null;
                        continue;
                    }
                    else
                    {
                        Log($"  -> GenderAge NOT matched for line: '{line}'");
                    }
                }

                if (isHE && current != null && pendingEventLen == null)
                {
                    if (line.Trim() == HebrewKlali || line.Trim() == HebrewKlaliReversed ||
                        line.Contains(HebrewKlali) || line.Contains(HebrewKlaliReversed))
                    {
                        Log($"  -> MATCH Klali (open category) - switching gender to none");

                        yield return current;

                        current = new IsrOrgCompetitionResult(
                            Competition: current.Competition,
                            AgeGroup: "open",
                            Date: current.Date,
                            Event: $"{current.EventStyleLen} {current.EventStyleName} - {HebrewKlali}",
                            EventStyleName: current.EventStyleName,
                            EventStyleLen: current.EventStyleLen,
                            EventStyleGender: "none",
                            EventStyleAge: "0",
                            PoolType: current.PoolType,
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW EVENT (Klali/Open): {current.Event}, gender=none");
                        continue;
                    }

                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);

                    if (genderAgeMatch.Success || mastersAgeMatch.Success)
                    {
                        var newAge = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["age"].Value
                            : mastersAgeMatch.Groups["age"].Value;
                        var newGender = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["gender"].Value.Trim()
                            : mastersAgeMatch.Groups["gender"].Value.Trim();
                        var newGenderNorm = HebrewTextHelper.NormalizeGenderHE(newGender);

                        if (newAge != current.EventStyleAge || newGenderNorm != current.EventStyleGender)
                        {
                            Log($"  -> MATCH GenderAge (category change): gender={newGender}, age={newAge}, genderNorm={newGenderNorm}");

                            yield return current;

                            current = new IsrOrgCompetitionResult(
                                Competition: current.Competition,
                                AgeGroup: newAge,
                                Date: current.Date,
                                Event: $"{current.EventStyleLen} {current.EventStyleName} - {newGender} {newAge}",
                                EventStyleName: current.EventStyleName,
                                EventStyleLen: current.EventStyleLen,
                                EventStyleGender: newGenderNorm,
                                EventStyleAge: newAge,
                                PoolType: current.PoolType,
                                Results: new List<IsrOrgResult>()
                            );

                            Log($"  -> NEW EVENT (category change): {current.Event}, gender={newGenderNorm}");
                            continue;
                        }
                    }
                }

                var rm_date = DateLineRx.Match(line);
                if (rm_date.Success)
                {
                    dat_relay = rm_date.Groups["date"].Value;
                    Log($"  -> DATE found: {dat_relay}");
                }

                if (isHE)
                {
                    // Masters-экспорт Maccabiah ARENA: английский заголовок заплыва без
                    // пола/возраста в строке. Только выставляем pending — сам заплыв
                    // (current) создаст следующая строка "מאסטרס <пол> <возраст>" через
                    // существующий механизм pendingEventLen / masters-continuation выше.
                    var enRelayHead = RelayHeaderEnInHE.Match(raw);
                    if (enRelayHead.Success)
                    {
                        pendingRelayLen = enRelayHead.Groups["len"].Value;
                        pendingRelayLegs = int.Parse(enRelayHead.Groups["legs"].Value);
                        pendingRelayStyleHe = CanonEnStyle(enRelayHead.Groups["style"].Value);
                        pendingEventLine = raw;
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        Log($"  -> PENDING RelayEN-in-HE: legs={pendingRelayLegs}, len={pendingRelayLen}, style={pendingRelayStyleHe}");
                        continue;
                    }

                    var enNoGenderHead = HeaderEnNoGenderInHE.Match(raw);
                    if (enNoGenderHead.Success)
                    {
                        pendingEventLen = NormalizeHeaderLen(enNoGenderHead.Groups["len"].Value);
                        pendingEventStyle = CanonEnStyle(enNoGenderHead.Groups["style"].Value);
                        pendingEventLine = raw;
                        pendingRelayLen = null;
                        pendingRelayStyleHe = null;
                        pendingRelayLegs = 0;
                        Log($"  -> PENDING HeaderEN-noGender-in-HE: len={pendingEventLen}, style={pendingEventStyle}");
                        continue;
                    }

                    var enHead = HeaderRxENinHE.Match(raw);
                    if (enHead.Success)
                    {
                        var ageClean = enHead.Groups["age"].Value.TrimStart('U', 'u');
                        var genderNorm = HebrewTextHelper.NormalizeGenderEN(enHead.Groups["gender"].Value);
                        // Приводим к нижнему регистру, чтобы стиль совпадал с ивритскими
                        // результатами того же соревнования ("freestyle", а не "Freestyle").
                        var styleNorm = HebrewTextHelper.NormalizeStyleName(
                            enHead.Groups["style"].Value.Trim().ToLowerInvariant());

                        Log($"  -> MATCH HeaderEN-in-HE: len={enHead.Groups["len"].Value}, style={styleNorm}, gender={genderNorm}, age={ageClean}");

                        pendingEventLen = null;
                        currentIsRelay = false;
                        currentRelayLegs = 0;

                        if (current != null) yield return current;

                        var nextEn = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dmEn = Regex.Match(nextEn, @"\d{2}/\d{2}/\d{4}");
                        var dateEn = dmEn.Success ? dmEn.Value : dat_relay;

                        current = new IsrOrgCompetitionResult(
                            Competition: lines[0].Trim(),
                            AgeGroup: ageClean,
                            Date: dateEn,
                            Event: raw,
                            EventStyleName: styleNorm,
                            EventStyleLen: enHead.Groups["len"].Value,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageClean,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW EVENT (EN-in-HE): {current.Event}, gender={genderNorm}");
                        continue;
                    }

                    // Тот же EN-заголовок, но категория без возраста ("- Men" / "- Women" /
                    // "- Men Para"). Пол и возраст разбирает ParseEnCategory — она же служит
                    // эстафетам и уже умеет Para (возраст "para", чтобы Men и Men Para не
                    // схлопнулись в одно событие).
                    var enCatHead = HeaderEnCategoryInHE.Match(raw);
                    if (enCatHead.Success)
                    {
                        var (genderCat, ageCat) = ParseEnCategory(enCatHead.Groups["cat"].Value);
                        if (genderCat != "none" || ageCat is "open" or "para")
                        {
                            var styleCat = HebrewTextHelper.NormalizeStyleName(
                                enCatHead.Groups["style"].Value.Trim().ToLowerInvariant());

                            Log($"  -> MATCH HeaderEN-category-in-HE: len={enCatHead.Groups["len"].Value}, " +
                                $"style={styleCat}, gender={genderCat}, age={ageCat}");

                            pendingEventLen = null;
                            currentIsRelay = false;
                            currentRelayLegs = 0;

                            if (current != null) yield return current;

                            var nextCat = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                            var dmCat = Regex.Match(nextCat, @"\d{2}/\d{2}/\d{4}");

                            current = new IsrOrgCompetitionResult(
                                Competition: lines[0].Trim(),
                                AgeGroup: ageCat,
                                Date: dmCat.Success ? dmCat.Value : dat_relay,
                                Event: raw,
                                EventStyleName: styleCat,
                                EventStyleLen: enCatHead.Groups["len"].Value,
                                EventStyleGender: genderCat,
                                EventStyleAge: ageCat,
                                PoolType: "25m",
                                Results: new List<IsrOrgResult>()
                            );
                            Log($"  -> NEW EVENT (EN-category-in-HE): {current.Event}, gender={genderCat}, age={ageCat}");
                            continue;
                        }
                    }

                    // Интернациональная эстафета Маккабиады: заголовок целиком на английском,
                    // с полом/возрастом в той же строке ("4X100m Freestyle Relay - U17 Girls"),
                    // без отдельной ивритской строки-продолжения (в отличие от masters-EN-в-HE
                    // выше). Матчим по СЫРОЙ строке — normalize-реверс её ломает.
                    var relEnFullHead = RelayHeaderEnFull.Match(raw);
                    if (relEnFullHead.Success)
                    {
                        var (genderNormRel, ageRel) = ParseEnCategory(relEnFullHead.Groups["cat"].Value);
                        var styleNormRel = CanonEnStyle(relEnFullHead.Groups["style"].Value);
                        int legsRel = int.Parse(relEnFullHead.Groups["legs"].Value);

                        Log($"  -> MATCH RelayHeaderEnFull-in-HE: legs={legsRel}, len={relEnFullHead.Groups["len"].Value}, style={styleNormRel}, gender={genderNormRel}, age={ageRel}");

                        pendingEventLen = null;
                        currentIsRelay = true;
                        currentRelayLegs = legsRel;

                        if (current != null) yield return current;

                        var nextRelEn = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dmRelEn = Regex.Match(nextRelEn, @"\d{2}/\d{2}/\d{4}");
                        var dateRelEn = dmRelEn.Success ? dmRelEn.Value : dat_relay;

                        current = new IsrOrgCompetitionResult(
                            Competition: lines[0].Trim(),
                            AgeGroup: ageRel,
                            Date: dateRelEn,
                            Event: raw,
                            EventStyleName: styleNormRel,
                            EventStyleLen: $"{legsRel}X{relEnFullHead.Groups["len"].Value}",
                            EventStyleGender: genderNormRel,
                            EventStyleAge: ageRel,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW RELAY EVENT (EN-in-HE): {current.Event}, gender={genderNormRel}");
                        continue;
                    }

                    var rm = RelayHeaderRxHE.Match(line);
                    var rm2 = RelayHeaderRxHE2.Match(line);

                    if (rm.Success || rm2.Success)
                    {
                        var match = rm.Success ? rm : rm2;
                        int legs = int.Parse(match.Groups["legs"].Value);
                        int legLen = int.Parse(match.Groups["len"].Value);

                        Log($"  -> MATCH RelayHeader: legs={legs}, len={legLen}, format={(rm.Success ? "1 (legsXlen)" : "2 (lenXlegs)")}");
                        pendingEventLen = null;
                        currentIsRelay = true;
                        currentRelayLegs = legs;

                        if (current != null) yield return current;

                        var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dateParts = next.Split(' ');
                        var date = dateParts.Length > 1 ? dateParts[1] : string.Empty;

                        if (!Regex.IsMatch(date, @"^\d{2}/\d{2}/\d{4}$"))
                        {
                            date = dat_relay;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(match.Groups["gender"].Value.Trim());
                        string lenRelay = $"{legs}X{legLen}";
                        var styleHe = match.Groups["style"].Value.Trim();
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(styleHe, styleHe);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);
                        var ageGroup = match.Groups["age"].Value;

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroup,
                            Date: date,
                            Event: line,
                            EventStyleName: styleNorm,
                            EventStyleLen: lenRelay,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroup,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW RELAY EVENT: {current.Event}, gender={genderNorm}");
                        continue;
                    }

                    // Эстафета с текстовой категорией вместо пола+возраста («- שומרי שבת …»).
                    var rmCat = RelayHeaderRxHECategory.Match(line);
                    if (rmCat.Success)
                    {
                        int legs = int.Parse(rmCat.Groups["legs"].Value);
                        int legLen = int.Parse(rmCat.Groups["len"].Value);
                        var catToken = HeCategoryToken(rmCat.Groups["cat"].Value.Trim());
                        Log($"  -> MATCH RelayHeader (текстовая категория): legs={legs}, len={legLen}, cat={catToken ?? "—"}");

                        pendingEventLen = null;
                        currentIsRelay = true;
                        currentRelayLegs = legs;
                        if (current != null) yield return current;

                        var nextCat = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dateCatParts = nextCat.Split(' ');
                        var dateCat = dateCatParts.Length > 1 ? dateCatParts[1] : string.Empty;
                        if (!Regex.IsMatch(dateCat, @"^\d{2}/\d{2}/\d{4}$")) dateCat = dat_relay;

                        var styleHeCat = rmCat.Groups["style"].Value.Trim();
                        var styleNormCat = HebrewTextHelper.NormalizeStyleName(
                            HebrewTextHelper.StyleMapHE.GetValueOrDefault(styleHeCat, styleHeCat));

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: string.Empty,       // в шапке возраста нет
                            Date: dateCat,
                            Event: line,
                            EventStyleName: styleNormCat,
                            EventStyleLen: $"{legs}X{legLen}",
                            EventStyleGender: "none",     // поплыв смешанный, пол берётся с пловца
                            EventStyleAge: catToken ?? string.Empty,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW RELAY EVENT (текстовая категория): {current.Event}");
                        continue;
                    }

                    if (RelayHeaderNoCategoryRxHE.IsMatch(line))
                    {
                        var mm = RelayHeaderNoCategoryRxHE.Match(line);
                        int legs = int.Parse(mm.Groups["legs"].Value);
                        int legLen = int.Parse(mm.Groups["len"].Value);
                        var styleHe = mm.Groups["style"].Value.Trim();

                        Log($"  -> MATCH RelayHeader (masters, no age): legs={legs}, len={legLen}, style={styleHe}");

                        // current обнуляется, иначе masters-continuation (строка возраста ниже)
                        // отдаст то же событие второй раз — дубль в выводе парсера.
                        if (current != null) yield return current;
                        current = null;

                        currentIsRelay = true;
                        currentRelayLegs = legs;

                        pendingRelayLegs = legs;
                        pendingRelayLen = $"{legs}X{legLen}";
                        pendingRelayStyleHe = styleHe;
                        pendingEventLine = line;

                        continue;
                    }
                }

                // Чистый EN-файл: заголовок с гибкой категорией после тире.
                if (!isHE)
                {
                    // ── Masters-EN экспорт loglig (зимние мастерс ARENA и т.п.): английский
                    // заголовок без пола/возраста ("400m Freestyle", "4X50m Freestyle Relay"),
                    // а категории идут ивритскими строками "מאסטרס <пол> <возраст>" /
                    // "מאסטרס שליחות <возраст>" — в сырых строках в RTL-реверсе, нормализуем.
                    // ВАЖНО: логика зеркалит HE-ветку (masters-continuation + category change),
                    // чтобы EN и HE стороны пары дали одинаковую последовательность событий:
                    // в частности, заголовок эстафеты с хвостом "Mix" НЕ считается новым
                    // событием (как "מיקס" в HE) — его результаты копятся в текущем.
                    var heNormLine = HebrewTextHelper.NormalizeHebrewLine(raw);
                    var mAgeEn = MastersAgeLineRxHE.Match(heNormLine);
                    var mRelayAgeEn = MastersRelayAgeLineRxHE.Match(heNormLine);

                    if (pendingRelayLen != null && (mAgeEn.Success || mRelayAgeEn.Success))
                    {
                        var ageEn = (mAgeEn.Success ? mAgeEn : mRelayAgeEn).Groups["age"].Value;
                        var genderEn = mAgeEn.Success
                            ? HebrewTextHelper.NormalizeGenderHE(mAgeEn.Groups["gender"].Value.Trim())
                            : "none";

                        if (current != null) yield return current;

                        currentIsRelay = true;
                        currentRelayLegs = pendingRelayLegs;
                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageEn,
                            Date: dat_relay,
                            Event: pendingEventLine ?? string.Empty,
                            EventStyleName: pendingRelayStyleHe!,
                            EventStyleLen: pendingRelayLen,
                            EventStyleGender: genderEn,
                            EventStyleAge: ageEn,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW RELAY EVENT (masters EN): {current.Event}, age={ageEn}");
                        pendingRelayLen = null;
                        pendingRelayStyleHe = null;
                        pendingRelayLegs = 0;
                        pendingEventLine = null;
                        continue;
                    }

                    if (pendingEventLen != null && mAgeEn.Success)
                    {
                        var ageEn = mAgeEn.Groups["age"].Value;
                        var genderEn = HebrewTextHelper.NormalizeGenderHE(mAgeEn.Groups["gender"].Value.Trim());

                        if (current != null) yield return current;

                        currentIsRelay = false;
                        currentRelayLegs = 0;
                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageEn,
                            Date: dat_relay,
                            Event: $"{pendingEventLen} {pendingEventStyle} - masters {genderEn} {ageEn}",
                            EventStyleName: pendingEventStyle!,
                            EventStyleLen: pendingEventLen,
                            EventStyleGender: genderEn,
                            EventStyleAge: ageEn,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW EVENT (masters EN): {current.Event}");
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        pendingEventLine = null;
                        continue;
                    }

                    if (pendingEventLen == null && current != null && mAgeEn.Success)
                    {
                        var newAgeEn = mAgeEn.Groups["age"].Value;
                        var newGenderEn = HebrewTextHelper.NormalizeGenderHE(mAgeEn.Groups["gender"].Value.Trim());

                        if (newAgeEn != current.EventStyleAge || newGenderEn != current.EventStyleGender)
                        {
                            yield return current;

                            current = new IsrOrgCompetitionResult(
                                Competition: current.Competition,
                                AgeGroup: newAgeEn,
                                Date: current.Date,
                                Event: $"{current.EventStyleLen} {current.EventStyleName} - masters {newGenderEn} {newAgeEn}",
                                EventStyleName: current.EventStyleName,
                                EventStyleLen: current.EventStyleLen,
                                EventStyleGender: newGenderEn,
                                EventStyleAge: newAgeEn,
                                PoolType: current.PoolType,
                                Results: new List<IsrOrgResult>()
                            );
                            Log($"  -> NEW EVENT (masters EN, category change): {current.Event}");
                            continue;
                        }
                    }

                    var enMastersRelayHead = RelayHeaderEnInHE.Match(line);
                    if (enMastersRelayHead.Success)
                    {
                        pendingRelayLegs = int.Parse(enMastersRelayHead.Groups["legs"].Value);
                        pendingRelayLen = $"{pendingRelayLegs}X{enMastersRelayHead.Groups["len"].Value}";
                        pendingRelayStyleHe = CanonEnStyle(enMastersRelayHead.Groups["style"].Value);
                        pendingEventLine = line;
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        Log($"  -> PENDING masters-EN relay header: len={pendingRelayLen}, style={pendingRelayStyleHe}");
                        continue;
                    }

                    var enMastersHead = HeaderEnNoGenderInHE.Match(line);
                    if (enMastersHead.Success)
                    {
                        pendingEventLen = NormalizeHeaderLen(enMastersHead.Groups["len"].Value);
                        pendingEventStyle = CanonEnStyle(enMastersHead.Groups["style"].Value);
                        pendingEventLine = line;
                        pendingRelayLen = null;
                        pendingRelayStyleHe = null;
                        pendingRelayLegs = 0;
                        Log($"  -> PENDING masters-EN header: len={pendingEventLen}, style={pendingEventStyle}");
                        continue;
                    }

                    var enRelayFull = RelayHeaderEnFull.Match(line);
                    var enIndivFull = enRelayFull.Success ? Match.Empty : HeaderEnFull.Match(line);
                    if (enRelayFull.Success || enIndivFull.Success)
                    {
                        bool relay = enRelayFull.Success;
                        var mm = relay ? enRelayFull : enIndivFull;
                        var (genderNorm, age) = ParseEnCategory(mm.Groups["cat"].Value);
                        var styleNorm = CanonEnStyle(mm.Groups["style"].Value);
                        var len = relay
                            ? $"{mm.Groups["legs"].Value}X{mm.Groups["len"].Value}"
                            : mm.Groups["len"].Value;

                        if (current != null) yield return current;

                        currentIsRelay = relay;
                        currentRelayLegs = relay ? int.Parse(mm.Groups["legs"].Value) : 0;
                        pendingEventLen = null;
                        pendingEventStyle = null;

                        // Дата события — из строки времени под заголовком ("05/07/2026 17:00").
                        var nextEn = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dmEn = Regex.Match(nextEn, @"\d{2}/\d{2}/\d{4}");
                        var dateEn = dmEn.Success ? dmEn.Value : dat_relay;

                        current = new IsrOrgCompetitionResult(
                            Competition: lines[0].Trim(),
                            AgeGroup: age,
                            Date: dateEn,
                            Event: line,
                            EventStyleName: styleNorm,
                            EventStyleLen: len,
                            EventStyleGender: genderNorm,
                            EventStyleAge: age,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW EN EVENT: {line} => style={styleNorm}, gender={genderNorm}, age={age}, relay={relay}");
                        continue;
                    }
                }

                var m = headerRx.Match(line);
                if (m.Success)
                {
                    Log($"  -> MATCH HeaderFormat1: len={m.Groups["len"].Value}, style={m.Groups["style"].Value}, gender={m.Groups["gender"].Value}, age={m.Groups["age"].Value}");
                    pendingEventLen = null;

                    var styleVal = m.Groups["style"].Value;
                    bool isRelayHeader =
                        (!isHE && styleVal.Contains("Relay", StringComparison.OrdinalIgnoreCase)) ||
                        (isHE && styleVal.Contains(HebrewRelay, StringComparison.OrdinalIgnoreCase));

                    currentIsRelay = isRelayHeader;
                    currentRelayLegs = isRelayHeader ? 4 : 0;

                    if (current != null)
                    {
                        yield return current;
                    }

                    var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                    var dateParts = next.Split(' ');
                    var date = dateParts.Length > 1 ? dateParts[1] : string.Empty;

                    if (!Regex.IsMatch(date, @"^\d{2}/\d{2}/\d{4}$"))
                    {
                        date = dat_relay;
                    }

                    var len = NormalizeHeaderLen(m.Groups["len"].Value);

                    string genderNorm = isHE
                        ? HebrewTextHelper.NormalizeGenderHE(m.Groups["gender"].Value)
                        : HebrewTextHelper.NormalizeGenderEN(m.Groups["gender"].Value);

                    current = new IsrOrgCompetitionResult(
                        Competition: isHE ? HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()) : lines[0].Trim(),
                        AgeGroup: m.Groups["age"].Value,
                        Date: date,
                        Event: line,
                        EventStyleName: HebrewTextHelper.NormalizeStyleName(
                            isHE
                                ? HebrewTextHelper.StyleMapHE.GetValueOrDefault(m.Groups["style"].Value, m.Groups["style"].Value)
                                : m.Groups["style"].Value),
                        EventStyleLen: len,
                        EventStyleGender: genderNorm,
                        EventStyleAge: m.Groups["age"].Value,
                        PoolType: "25m",
                        Results: new List<IsrOrgResult>()
                    );
                    Log($"  -> NEW EVENT (Format1): {current.Event}");
                    continue;
                }

                // Личный заплыв с текстовой категорией («200 חופשי - שומרי שבת מוקדמות צעירים»).
                // Только для известного стиля: иначе шаблон подберёт любую строку с дефисом.
                if (isHE)
                {
                    var catMatch = HeaderRxHECategory.Match(line);
                    if (catMatch.Success)
                    {
                        var styleHeRaw = catMatch.Groups["style"].Value.Trim();
                        if (HebrewTextHelper.StyleMapHE.TryGetValue(styleHeRaw, out var styleMapped))
                        {
                            var catToken = HeCategoryToken(catMatch.Groups["cat"].Value.Trim());
                            Log($"  -> MATCH HeaderCategory: len={catMatch.Groups["len"].Value}, style={styleHeRaw}, cat={catToken ?? "—"}");

                            pendingEventLen = null;
                            currentIsRelay = false;
                            currentRelayLegs = 0;
                            if (current != null) yield return current;

                            var nextCat = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                            var dateCatParts = nextCat.Split(' ');
                            var dateCat = dateCatParts.Length > 1 ? dateCatParts[1] : string.Empty;
                            if (!Regex.IsMatch(dateCat, @"^\d{2}/\d{2}/\d{4}$")) dateCat = dat_relay;

                            current = new IsrOrgCompetitionResult(
                                Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                                AgeGroup: string.Empty,       // в шапке возраста нет
                                Date: dateCat,
                                Event: line,
                                EventStyleName: HebrewTextHelper.NormalizeStyleName(styleMapped),
                                EventStyleLen: NormalizeHeaderLen(catMatch.Groups["len"].Value),
                                EventStyleGender: "none",     // поплыв смешанный, пол берётся с пловца
                                EventStyleAge: catToken ?? string.Empty,
                                PoolType: "25m",
                                Results: new List<IsrOrgResult>()
                            );
                            Log($"  -> NEW EVENT (текстовая категория): {current.Event}");
                            continue;
                        }
                        Log($"  -> HeaderCategory REJECTED (неизвестный стиль '{styleHeRaw}')");
                    }

                    var simpleMatch = HeaderRxHESimple.Match(line);
                    if (simpleMatch.Success)
                    {
                        var styleCheck = simpleMatch.Groups["style"].Value.Trim();
                        Log($"  -> SimpleHeader candidate: len={simpleMatch.Groups["len"].Value}, style='{styleCheck}'");

                        if (!styleCheck.Contains("מיקום") &&
                            !styleCheck.Contains("מקצה") &&
                            !styleCheck.Contains("תוצאות"))
                        {
                            pendingEventLen = NormalizeHeaderLen(simpleMatch.Groups["len"].Value);
                            pendingEventStyle = styleCheck;
                            pendingEventLine = line;
                            Log($"  -> PENDING SimpleHeader: len={pendingEventLen}, style={pendingEventStyle}");
                            continue;
                        }
                        else
                        {
                            Log($"  -> SimpleHeader REJECTED (table header)");
                        }
                    }
                }

                // Маккаби-экспорт loglig: заголовок эстафеты вовсе без категории
                // («4X50 חופשי שליחים»), и строки «מאסטרס …», которая довесила бы пол/возраст
                // (masters-механика pendingRelay*), не будет — сразу идут команды. Первая же
                // командная строка материализует событие из pending: пол и возрастная полоса
                // в таком протоколе не печатаются (места сквозные по всей дисциплине),
                // поэтому gender="none", возраст пустой — как у эстафет с текстовой категорией.
                if (isHE && pendingRelayLen != null && (current == null || !currentIsRelay)
                    && RelayTeamLineRxHE.IsMatch(line))
                {
                    if (current != null) yield return current;

                    currentIsRelay = true;
                    currentRelayLegs = pendingRelayLegs;
                    var styleHePending = pendingRelayStyleHe!;
                    current = new IsrOrgCompetitionResult(
                        Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                        AgeGroup: string.Empty,
                        Date: dat_relay,
                        Event: pendingEventLine ?? string.Empty,
                        EventStyleName: HebrewTextHelper.NormalizeStyleName(
                            HebrewTextHelper.StyleMapHE.GetValueOrDefault(styleHePending, styleHePending)),
                        EventStyleLen: pendingRelayLen,
                        EventStyleGender: "none",
                        EventStyleAge: string.Empty,
                        PoolType: "25m",
                        Results: new List<IsrOrgResult>()
                    );
                    Log($"  -> NEW RELAY EVENT (no category, materialized by first team line): {current.Event}");
                    pendingRelayLen = null;
                    pendingRelayStyleHe = null;
                    pendingRelayLegs = 0;
                    pendingEventLine = null;
                    // Без continue: эта же строка тут же разбирается как команда ниже.
                }

                if (current != null && currentIsRelay)
                {
                    var tm = RelayTeamLineRxHE.Match(line);
                    if (tm.Success)
                    {
                        Log($"  -> MATCH RelayTeam: pos={tm.Groups["pos"].Value}, heat={tm.Groups["heat"].Value}");
                        int pos = int.Parse(tm.Groups["pos"].Value);
                        int heat = int.Parse(tm.Groups["heat"].Value);
                        int lane = int.Parse(tm.Groups["lane"].Value);
                        string team = tm.Groups["team"].Value.Trim();

                        string timeTok = tm.Groups["time"].Value.Trim();
                        string? time = null;
                        string? timeFailNote = null;

                        if (Regex.IsMatch(timeTok, @"^(?:\d{1,2}:)?\d{2}:\d{2}\.\d{1,2}$"))
                        {
                            if (!IsrOrgResultLineParser.IsZeroTime(timeTok))
                            {
                                time = timeTok;
                            }
                        }
                        else if (timeTok is "DQ" or "NS" or "DNF" or "DNS")
                        {
                            timeFailNote = timeTok;
                        }

                        var swimmers = new List<RelaySwimmer>();
                        int k = i + 1;
                        int order = 1;

                        while (k < lines.Count && swimmers.Count < currentRelayLegs)
                        {
                            var sRaw = lines[k].Trim();
                            var sLine = HebrewTextHelper.NormalizeHebrewLine(sRaw);

                            // Состав недобран, а началось следующее событие — дальше не идём:
                            // иначе заголовок и результаты чужого заплыва уедут в эстафету
                            // (год рождения в строке результата выглядит как год ноги).
                            if (IsAnyEventHeaderHE(sLine))
                            {
                                Log($"  -> Состав эстафеты оборван новым заголовком: '{sLine}'");
                                break;
                            }

                            // ...и точно так же — на строке СЛЕДУЮЩЕЙ КОМАНДЫ. Если у команды в
                            // её блоке меньше ног, чем ожидается (перенос имени на две строки),
                            // скан уходил в чужой блок, забирал оттуда ноги и перепрыгивал через
                            // строку той команды — она пропадала молча. Найдено ретро-аудитом
                            // 2026-08-03: в соревновании 1511 так терялись места 4, 10 и 16.
                            // Лучше отдать неполный состав, чем потерять команду целиком.
                            if (RelayTeamLineRxHE.IsMatch(sLine))
                            {
                                Log($"  -> Состав эстафеты оборван строкой следующей команды: '{sLine}'");
                                break;
                            }

                            if (Regex.IsMatch(sLine, @"\b\d{4}\b"))
                            {
                                swimmers.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(sLine, order));
                                order++;
                            }

                            k++;
                        }

                        i = k - 1;

                        if (swimmers.Count >= currentRelayLegs)
                        {
                            current.Results.Add(new IsrOrgResult(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
                                TimeFailNote: timeFailNote,
                                InternationalPoints: 0,
                                IsRelay: true,
                                RelayTeamName: team,
                                RelaySwimmersName: string.Join(", ", swimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
                                RelaySwimmers: swimmers
                            ));
                            Log($"  -> Added relay result: team={team}");
                        }
                        else
                        {
                            pendingRelayResult = new IsrOrgResult(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
                                TimeFailNote: timeFailNote,
                                InternationalPoints: 0,
                                IsRelay: true,
                                RelayTeamName: team,
                                RelaySwimmersName: null,
                                RelaySwimmers: null
                            );
                            pendingSwimmers = swimmers;
                            pendingSwimmersOrder = order;
                        }

                        continue;
                    }
                }

                // EN-эстафета: командный результат (место/команда/время). Ниже, если
                // строки пловцов удалось восстановить в ParseCompetitionsInternal
                // (см. ReconstructEnRelaySwimmerNames — там переносы имён по X-колонкам
                // Last/First name склеиваются обратно в "LAST First Year"), подбираем
                // до currentRelayLegs таких строк для состава эстафеты. Если строк
                // меньше ожидаемого или формат не совпал — состав остаётся null
                // (garbage-in-garbage-out недопустим, лучше пустой состав).
                // Матчим по СЫРОЙ строке (не по normalize-реверснутой `line`): в EN-режиме
                // raw==line, а интернациональная эстафета внутри HE-экспорта тоже печатается
                // англоязычной строкой "heat lane team time Rank pos" — normalize-реверс
                // (рассчитанный на иврит) ломает её порядок токенов.
                if (current != null && currentIsRelay)
                {
                    var tmEn = RelayTeamLineEn.Match(raw);
                    if (tmEn.Success)
                    {
                        int pos = int.Parse(tmEn.Groups["pos"].Value);
                        int heat = int.Parse(tmEn.Groups["heat"].Value);
                        int lane = int.Parse(tmEn.Groups["lane"].Value);
                        string team = tmEn.Groups["team"].Value.Trim();
                        string timeTok = tmEn.Groups["time"].Value.Trim();

                        string? time = null;
                        string? timeFailNote = null;
                        if (Regex.IsMatch(timeTok, @"^(?:\d{1,2}:)?\d{2}:\d{2}\.\d{1,2}$"))
                        {
                            if (!IsrOrgResultLineParser.IsZeroTime(timeTok)) time = timeTok;
                        }
                        else if (timeTok is "DQ" or "NS")
                        {
                            timeFailNote = timeTok;
                        }

                        List<RelaySwimmer>? enSwimmers = null;
                        if (currentRelayLegs > 0)
                        {
                            var candidates = new List<RelaySwimmer>();
                            int k = i + 1;
                            int order = 1;
                            while (k < lines.Count && candidates.Count < currentRelayLegs)
                            {
                                var sRaw = lines[k].Trim();
                                if (RelayTeamLineEn.IsMatch(sRaw)) break; // следующая команда — стоп
                                if (Regex.IsMatch(sRaw, @"^[\p{L}][\p{L}'\-]*\s+[\p{L}][\p{L}'\-]*\s+\d{4}$"))
                                {
                                    candidates.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(sRaw, order));
                                    order++;
                                }
                                k++;
                            }

                            // Разрыв страницы посреди состава команды: 4-ногая таблица
                            // может быть отпечатана СПОСОБОМ, при котором первые 1-3 ноги
                            // остаются на текущей странице, а остаток — в начале следующей,
                            // без повтора заголовка команды/колонок (см. кейс "Maccabiah MIX"
                            // 4X50, comp 1484 — HARAS/BENTES на одной странице, DABBAH/ACUNA
                            // на следующей). Если ног не хватило и текущая страница закончилась
                            // (а не просто наткнулись на новую команду/не-лег строку) —
                            // дособираем недостающие ноги с начала следующей страницы.
                            bool crossedPage = false;
                            int nextPageLinesConsumed = 0;
                            if (candidates.Count < currentRelayLegs && k >= lines.Count && pageIdx + 1 < pages.Count)
                            {
                                var nextLines = pages[pageIdx + 1];
                                int k2 = 0;
                                int nonMatchStreak = 0;
                                // Небольшой запас непарных строк-обрывков (см. ReconstructEnRelaySwimmerNames —
                                // фрагмент имени, для которого на этой странице не нашлось соседа для склейки,
                                // остаётся отдельной "шумовой" строкой из одного слова и не должен обрывать
                                // сбор ног). Явная строка команды/нового события — однозначный стоп-маркер.
                                while (k2 < nextLines.Count && candidates.Count < currentRelayLegs)
                                {
                                    var sRaw = nextLines[k2].Trim();
                                    if (RelayTeamLineEn.IsMatch(sRaw)) break; // новая команда — стоп
                                    if (RelayHeaderEnFull.IsMatch(sRaw) || HeaderEnFull.IsMatch(sRaw)) break; // новое событие — стоп

                                    if (Regex.IsMatch(sRaw, @"^[\p{L}][\p{L}'\-]*\s+[\p{L}][\p{L}'\-]*\s+\d{4}$"))
                                    {
                                        // Строка "выглядит" как готовая нога (два слова + год), НО если ей
                                        // непосредственно предшествует непристыкованный обрывок имени (шум,
                                        // который реконструкция НЕ смогла склеить в пределах этой страницы —
                                        // склейка-с-предыдущей-страницы в ReconstructEnRelaySwimmerNames не
                                        // поддерживается, у неё нет контекста прошлой страницы), это явный
                                        // признак, что "готовая" строка сама — недостроенный фрагмент (напр.
                                        // "KOZUC" / "HOWIC Micael 2009" / "Z" — читается как KOZUCHOWICZ, но
                                        // мы не можем это надёжно доказать). Гарантированно неоднозначный
                                        // случай — консервативно останавливаем сборку состава здесь, а не
                                        // подсовываем частично verно ногу.
                                        if (nonMatchStreak > 0) break;

                                        candidates.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(sRaw, order));
                                        order++;
                                        nonMatchStreak = 0;
                                    }
                                    else if (IsNameFragment(sRaw))
                                    {
                                        // Непристыкованный обрывок имени (шум реконструкции) — пропускаем.
                                        nonMatchStreak++;
                                    }
                                    else
                                    {
                                        break; // явно не строка ноги и не шумовой обрывок — стоп (safety)
                                    }

                                    if (nonMatchStreak > 4) break; // защита от рантвэя по несвязанной странице
                                    k2++;
                                }

                                if (candidates.Count == currentRelayLegs)
                                {
                                    crossedPage = true;
                                    nextPageLinesConsumed = k2;
                                }
                            }

                            if (candidates.Count == currentRelayLegs)
                            {
                                enSwimmers = candidates;
                                if (crossedPage)
                                {
                                    skipLeadingLinesNextPage = nextPageLinesConsumed;
                                    i = lines.Count; // текущая страница исчерпана этой командой
                                }
                                else
                                {
                                    i = k - 1;
                                }
                            }
                        }

                        current.Results.Add(new IsrOrgResult(
                            Country: "",
                            Position: pos,
                            Heat: heat,
                            Lane: lane,
                            LastName: "",
                            FirstName: "",
                            BirthYear: 0,
                            Club: team,
                            Time: time,
                            TimeFailNote: timeFailNote,
                            InternationalPoints: 0,
                            IsRelay: true,
                            RelayTeamName: team,
                            RelaySwimmersName: enSwimmers is null
                                ? null
                                : string.Join(", ", enSwimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
                            RelaySwimmers: enSwimmers
                        ));
                        Log($"  -> Added EN relay team result: pos={pos}, team='{team}', time={time}, legs={enSwimmers?.Count ?? 0}");
                        continue;
                    }
                }

                // EN-строка команды эстафеты ВНЕ отслеживаемого relay-события — секции
                // "...Relay Mix", заголовок которых сознательно не считается новым событием
                // (зеркало HE, где "מיקס"-заголовок не матчится и команды пропадают молча).
                // Пропускаем явно: пустая команда ("1 1 02:19.73 Rank 4") иначе ложно
                // срабатывает как строка личного результата и валит парс.
                if (!isHE && RelayTeamLineEn.IsMatch(line))
                {
                    Log($"  -> Skipped relay team line outside tracked relay event");
                    continue;
                }

                // ==== Разбор строки результата с авто-детектом ориентации токенов ====
                // Латиница строк результатов в двуязычных экспортах Maccabiah приходит
                // в РАЗНОМ порядке: в одних файлах уже нормальном (rank heat lane FAMILY
                // name year club time points), в других — перевёрнутом (восстанавливается
                // RTL-реверсом). Строки-места из трёх чисел ("2 7 3") матчат стартовый
                // детектор в ОБЕИХ ориентациях, поэтому выбираем ту, чья склейка со
                // следующей строкой реально совпала с FullResultRx. Норм (перевёрнутая)
                // имеет приоритет при равенстве → старые файлы не задеты.
                if (current != null)
                {
                    bool startNorm = Regex.IsMatch(line, @"^(-|\d+)\s+\d+\s+\d+");
                    bool startRaw = isHE && Regex.IsMatch(raw, @"^(-|\d+)\s+\d+\s+\d+");

                    if (startNorm || startRaw)
                    {
                        int resultLineIdx = i;

                        // Собирает entry в заданной ориентации, при необходимости
                        // подклеивая следующую строку (место и данные бывают разнесены).
                        (string entry, bool full, bool consumed) BuildEntry(bool useNorm)
                        {
                            string Prep(string s) => useNorm && isHE ? HebrewTextHelper.NormalizeHebrewLine(s) : s;
                            var e = Prep(raw);
                            bool cons = false;
                            if (!FullResultRx.IsMatch(e) && i + 1 < lines.Count)
                            {
                                // Строку смены категории ("מאסטרס נ 60-64", "בנות 13" и т.п.)
                                // подклеивать нельзя: не-полная строка результата (напр. DQ
                                // "…DQ / SW 4.4 0") съела бы её, событие не разделилось бы,
                                // и результаты соседних возрастов слились бы в одно событие.
                                var nextRaw = lines[i + 1].Trim();
                                var nextNorm = HebrewTextHelper.NormalizeHebrewLine(nextRaw);
                                bool nextIsCategory = MastersAgeLineRxHE.IsMatch(nextNorm)
                                    || MastersRelayAgeLineRxHE.IsMatch(nextNorm)
                                    || GenderAgeLineRxHE.IsMatch(nextNorm);
                                if (!nextIsCategory)
                                {
                                    e += " " + Prep(nextRaw);
                                    cons = true;
                                }
                            }
                            return (e, FullResultRx.IsMatch(e), cons);
                        }

                        var normC = startNorm ? BuildEntry(true) : (entry: "", full: false, consumed: false);
                        var rawC = startRaw ? BuildEntry(false) : (entry: "", full: false, consumed: false);

                        // Приоритет — ориентация, реально совпавшая с FullResultRx.
                        // Если ни одна, но норм-стартовала — легаси-путь (склейка+парс
                        // без полного совпадения) для сохранения прежнего поведения.
                        // Стартовый детектор из трёх чисел не привязан к границам
                        // токенов, поэтому ловит "1 4 02" внутри relay-строки
                        // "1 4 02:34.24 Rank 9". Чтобы такие строки не уходили в парсер,
                        // СЫРАЯ ориентация берётся ТОЛЬКО при реальном совпадении с
                        // FullResultRx. Легаси-парс-без-совпадения — лишь для norm.
                        bool useNormFinal;
                        bool proceed = true;
                        if (normC.full) useNormFinal = true;
                        else if (rawC.full) useNormFinal = false;
                        else if (startNorm) useNormFinal = true;
                        else { useNormFinal = true; proceed = false; }

                        if (proceed)
                        {
                            var chosen = useNormFinal ? normC : rawC;
                            var entry = chosen.entry;
                            if (chosen.consumed) i++;

                            Log($"  -> Result line candidate ({(useNormFinal ? "reversed/norm" : "raw")} orientation)");

                            try
                            {
                                var res = IsrOrgResultLineParser.ParseResultLine(entry);

                                // Перенос длинной фамилии: PDF иногда разносит её на соседние
                                // строки (напр. "TSCHERKOWS" / данные / "KI"), и в строке данных
                                // фамилия пустая. Восстанавливаем из соседних строк-фрагментов
                                // (только буквы) в той же ориентации, склеивая prev+next.
                                if (string.IsNullOrWhiteSpace(res.LastName))
                                {
                                    string Prep(string s) => useNormFinal ? NormalizeIfHe(s, isHE) : s;
                                    var prevFrag = resultLineIdx - 1 >= 0 ? Prep(lines[resultLineIdx - 1].Trim()) : string.Empty;
                                    var nextFrag = i + 1 < lines.Count ? Prep(lines[i + 1].Trim()) : string.Empty;

                                    var recovered = string.Empty;
                                    if (IsNameFragment(prevFrag)) recovered += prevFrag;
                                    if (IsNameFragment(nextFrag)) recovered += nextFrag;

                                    if (recovered.Length > 0)
                                    {
                                        res = res with { LastName = recovered };
                                        Log($"  -> Recovered wrapped surname: '{recovered}' (prev='{prevFrag}', next='{nextFrag}')");
                                    }
                                }

                                current.Results.Add(res);
                                Log($"  -> Added result: {res.LastName} {res.FirstName}, time={res.Time}");
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Parse error on page {pageNumber}, line '{entry}': {ex.Message}", ex);
                            }
                        }
                    }
                }
            }
        }

        if (pendingRelayResult != null && pendingSwimmers != null && current != null && pendingSwimmers.Count > 0)
        {
            current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
        }

        if (current != null)
        {
            Log($"Yielding final event: {current.Event} with {current.Results.Count} results");
            yield return current;
        }

        Log($"Parse complete. Total events yielded.");
    }

    private static IsrOrgResult CreateRelayResult(IsrOrgResult pending, List<RelaySwimmer> swimmers)
    {
        return new IsrOrgResult(
            Country: pending.Country,
            Position: pending.Position,
            Heat: pending.Heat,
            Lane: pending.Lane,
            LastName: pending.LastName,
            FirstName: pending.FirstName,
            BirthYear: pending.BirthYear,
            Club: pending.Club,
            Time: pending.Time,
            TimeFailNote: pending.TimeFailNote,
            InternationalPoints: pending.InternationalPoints,
            IsRelay: true,
            RelayTeamName: pending.RelayTeamName,
            RelaySwimmersName: string.Join(", ", swimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
            RelaySwimmers: swimmers
        );
    }
}
