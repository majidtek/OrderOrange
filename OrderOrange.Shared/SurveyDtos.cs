namespace OrderOrange.Shared;

// ---------- Surveys: a store asks its guests, the guests tick answers ----------

/// <summary>One question with the answers a guest may pick from.</summary>
public record SurveyQuestionDto(
    string Id,
    string Text,
    List<string> Options,
    // Several ticks allowed (checkboxes) instead of one (radio).
    bool Multi = false);

/// <summary>A survey as the store sees it in its list.</summary>
public record SurveyDto(
    string Id,
    string Title,
    bool IsActive,
    DateTime CreatedAt,
    List<SurveyQuestionDto> Questions,
    int ResponseCount = 0);

public record SaveSurveyRequest(string Title, bool IsActive, List<SurveyQuestionDto> Questions);

/// <summary>The survey as a GUEST sees it — the store's face and the questions, nothing else.</summary>
public record PublicSurveyDto(
    string Id,
    int StoreId,
    string StoreName,
    string? LogoData,
    string Title,
    List<SurveyQuestionDto> Questions);

/// <summary>One guest's ticks for one question: the indexes of the chosen options.</summary>
public record SurveyAnswerDto(string QuestionId, List<int> Selected);

public record SubmitSurveyRequest(
    List<SurveyAnswerDto> Answers,
    string? Comment = null,
    // Where the guest came from: a table code when they scanned one, else empty.
    string? Source = null);

/// <summary>How many guests picked each option — the store's results view.</summary>
public record SurveyQuestionResultDto(string Id, string Text, List<string> Options, List<int> Counts, bool Multi);

public record SurveyResultsDto(
    string Id,
    string Title,
    int Responses,
    List<SurveyQuestionResultDto> Questions,
    // The free-text comments guests left, newest first (capped).
    List<SurveyCommentDto> Comments);

public record SurveyCommentDto(DateTime At, string Text, string? Source);
