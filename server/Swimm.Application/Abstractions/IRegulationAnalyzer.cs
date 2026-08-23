using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Разбор регламента соревнования (תקנון, PDF) ради флагов, которые иначе админ ставит
/// руками: вручаются ли медали, ведётся ли клубный зачёт, чемпионат ли это Израиля.
///
/// Регламент — чужой документ произвольной формы, поэтому анализатор ничего не решает сам:
/// он возвращает находки С ЦИТАТАМИ, а галочки проставляет человек (мы лишь предлагаем).
/// </summary>
public interface IRegulationAnalyzer
{
    RegulationAnalysisDto Analyze(Stream pdfStream, string fileName);
}
