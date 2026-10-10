namespace Swimm.Application.Mapping;

/// <summary>Что сделать с английским именем пловца (см. <see cref="EnglishNameRepairPlanner"/>).</summary>
public enum EnglishNameRepairAction
{
    /// <summary>Записать имя с карточки loglig.</summary>
    Write,

    /// <summary>Очистить EN-поля: текущее имя точно не его, а взять верное неоткуда.</summary>
    Clear,

    /// <summary>Оставить как есть — решать человеку (или loglig пишет так же).</summary>
    Keep,
}

/// <summary>Решение по одному пловцу: действие, новые EN-поля (для <c>Write</c>) и почему.</summary>
public sealed record EnglishNameRepairDecision(
    EnglishNameRepairAction Action, string LastEn, string FirstEn, string Reason);

/// <summary>
/// Как чинить английское имя, которое не подходит к ивритскому (10.10.2026,
/// docs/plans/en-names-handoff.md). Источник истины — английская карточка loglig (решение Влада
/// 08.10, И-47); здесь только ВЫБОР, без сети и базы, чтобы правила проверялись тестами.
///
/// Порядок:
/// <list type="number">
///   <item>Карточка подходит — разрез <see cref="EnglishNameFit.SplitLatinName"/> однозначен и
///     сам проходит <see cref="EnglishNameFit.Assess"/> → <b>Write</b>.</item>
///   <item>Карточка пишет те же слова, что уже стоят (прозвище, второе имя, своя
///     транслитерация loglig: «MOTI» у «מרדכי») → <b>Keep</b>: это не ошибка импорта, находку
///     проверки закрывают «Принять».</item>
///   <item>В латинском поле иврит → <b>Clear</b>: иврит в EN не бывает верным никогда, а
///     пустое поле витрина покажет ивритом и дозаполнит следующий протокол.</item>
///   <item>Имя чужое, а карточка есть, но не подходит (имя на ней тоже другое: у «פוגל דודוביץ
///     איתן» — «ADAM FOGEL») → <b>Clear</b>: текущее имя точно соседское, верного нет нигде.</item>
///   <item>Имя чужое, карточки нет → <b>Keep</b>: без второго источника не трогаем, руками.</item>
/// </list>
/// Пустое лучше чужого не только для витрины: импорт дозаполняет ТОЛЬКО пустые EN-поля, так
/// что чужое имя никто никогда не перезапишет, а пустое — перезапишет первый же протокол.
/// </summary>
public static class EnglishNameRepairPlanner
{
    public static EnglishNameRepairDecision Decide(
        string heLast, string heFirst, string oldLast, string oldFirst, string? card)
    {
        var (verdict, word, _) = EnglishNameFit.Assess(heLast, heFirst, oldLast, oldFirst);
        if (verdict is EnglishNameVerdict.Fits or EnglishNameVerdict.Nothing)
            return new(EnglishNameRepairAction.Keep, oldLast, oldFirst, "имя подходит — чинить нечего");

        var what = verdict == EnglishNameVerdict.HebrewInLatin ? "иврит в латинском поле" : $"«{word}» чужое";

        // Латиницы у игрока на loglig нет — заголовок английской карточки приходит на иврите
        // («יסמין גלסמן»). Это не источник английского имени: считаем, что карточки нет.
        if (card is not null && card.Any(c => c is >= '֐' and <= '׿')) card = null;

        if (card is not null
            && EnglishNameFit.SplitLatinName(heLast, heFirst, card) is { } split
            && EnglishNameFit.Assess(heLast, heFirst, split.Last, split.First).Verdict == EnglishNameVerdict.Fits)
            return new(EnglishNameRepairAction.Write, split.Last, split.First, $"{what}; карточка loglig «{card}»");

        if (card is not null && SameWords(card, $"{oldLast} {oldFirst}"))
            return new(EnglishNameRepairAction.Keep, oldLast, oldFirst,
                $"{what}, но карточка loglig пишет так же («{card}») — прозвище или своё написание, «Принять»");

        if (verdict == EnglishNameVerdict.HebrewInLatin)
            return new(EnglishNameRepairAction.Clear, "", "",
                card is null ? $"{what}; карточки нет" : $"{what}; карточка «{card}» не подходит");

        return card is null
            ? new(EnglishNameRepairAction.Keep, oldLast, oldFirst, $"{what}; карточки нет — руками")
            : new(EnglishNameRepairAction.Clear, "", "", $"{what}; карточка «{card}» тоже не подходит");
    }

    /// <summary>Те же слова в любом порядке, без учёта регистра и дефисов.</summary>
    internal static bool SameWords(string a, string b)
    {
        static string Norm(string s) => string.Join(' ',
            s.ToUpperInvariant().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Order());
        return Norm(a) == Norm(b);
    }
}
