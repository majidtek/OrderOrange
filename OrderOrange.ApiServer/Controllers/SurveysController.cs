using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// A store asks its guests questions; guests tick answers. The store side is behind
/// the <see cref="Perm.Surveys"/> door; the guest side is public and anonymous — a
/// scanned table card or the shop page is all the invitation there is.
/// </summary>
public class SurveysController(AppDbContext db, SurveyStore surveys, IConfiguration config) : ApiControllerBase
{
    // ---------- The store's side ----------

    [HttpGet]
    [RequirePerm(Perm.Surveys)]
    public async Task<ActionResult<List<SurveyDto>>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var docs = await surveys.ListAsync(CurrentRestaurantId);
        var counts = await surveys.ResponseCountsAsync(docs.Select(d => d.Id));
        return Ok(docs.Select(d => ToDto(d, counts.GetValueOrDefault(d.Id))).ToList());
    }

    [HttpPost]
    [RequirePerm(Perm.Surveys)]
    public async Task<ActionResult<SurveyDto>> Create(SaveSurveyRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var error = Validate(req, out var questions);
        if (error is not null) return BadRequest(new { code = error });
        if (await surveys.CountAsync(CurrentRestaurantId) >= 30)
            return BadRequest(new { message = "That is as many surveys as one store may keep." });

        var doc = new SurveyDoc
        {
            Id = ObjectId.GenerateNewId(),
            RestaurantId = CurrentRestaurantId,
            Title = req.Title.Trim(),
            IsActive = req.IsActive,
            CreatedAt = DateTime.Now,
            Questions = questions,
        };
        await surveys.InsertAsync(doc);
        return Ok(ToDto(doc, 0));
    }

    [HttpPut("{id}")]
    [RequirePerm(Perm.Surveys)]
    public async Task<ActionResult<SurveyDto>> Update(string id, SaveSurveyRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await surveys.GetAsync(CurrentRestaurantId, oid);
        if (doc is null) return NotFound();
        var error = Validate(req, out var questions);
        if (error is not null) return BadRequest(new { code = error });

        doc.Title = req.Title.Trim();
        doc.IsActive = req.IsActive;
        doc.Questions = questions;
        await surveys.ReplaceAsync(doc);
        var counts = await surveys.ResponseCountsAsync([doc.Id]);
        return Ok(ToDto(doc, counts.GetValueOrDefault(doc.Id)));
    }

    [HttpDelete("{id}")]
    [RequirePerm(Perm.Surveys)]
    public async Task<IActionResult> Delete(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        if (await surveys.GetAsync(CurrentRestaurantId, oid) is null) return NotFound();
        await surveys.DeleteAsync(CurrentRestaurantId, oid);
        return NoContent();
    }

    /// <summary>How the guests answered: a count per option, plus their written comments.</summary>
    [HttpGet("{id}/results")]
    [RequirePerm(Perm.Surveys)]
    public async Task<ActionResult<SurveyResultsDto>> Results(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await surveys.GetAsync(CurrentRestaurantId, oid);
        if (doc is null) return NotFound();

        var responses = await surveys.ResponsesAsync(doc.Id);
        var results = doc.Questions.Select(q =>
        {
            var counts = new int[q.Options.Count];
            foreach (var r in responses)
            foreach (var a in r.Answers.Where(a => a.QuestionId == q.Id))
            foreach (var i in a.Selected.Distinct())
                if (i >= 0 && i < counts.Length) counts[i]++;
            return new SurveyQuestionResultDto(q.Id, q.Text, q.Options, counts.ToList(), q.Multi);
        }).ToList();

        var comments = responses
            .Where(r => !string.IsNullOrWhiteSpace(r.Comment))
            .Take(200)
            .Select(r => new SurveyCommentDto(r.At, r.Comment!.Trim(), r.Source))
            .ToList();

        return Ok(new SurveyResultsDto(doc.Id.ToString(), doc.Title, responses.Count, results, comments));
    }

    // ---------- The guest's side ----------

    /// <summary>The store's live survey, or 204 when it is not asking anything right now.</summary>
    [HttpGet("public/{storeId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicSurveyDto>> Public(int storeId)
    {
        var doc = await surveys.ActiveAsync(storeId);
        if (doc is null) return NoContent();
        var store = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId && r.IsApproved);
        if (store is null) return NotFound();
        return Ok(new PublicSurveyDto(doc.Id.ToString(), storeId, store.Name,
            MediaLinks.Logo(config, store.Id, store.LogoData), doc.Title,
            doc.Questions.Select(ToDto).ToList()));
    }

    [HttpPost("public/{storeId:int}/{id}/answer")]
    [AllowAnonymous]
    public async Task<IActionResult> Answer(int storeId, string id, SubmitSurveyRequest req)
    {
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await surveys.GetAsync(storeId, oid);
        if (doc is null || !doc.IsActive) return NotFound();

        // Only ticks that exist: a known question, an option index inside its list.
        var byId = doc.Questions.ToDictionary(q => q.Id);
        var answers = new List<SurveyAnswerDoc>();
        foreach (var a in req.Answers ?? [])
        {
            if (!byId.TryGetValue(a.QuestionId, out var q)) continue;
            var picks = (a.Selected ?? []).Where(i => i >= 0 && i < q.Options.Count).Distinct().ToList();
            if (picks.Count == 0) continue;
            if (!q.Multi) picks = [picks[0]];
            answers.Add(new SurveyAnswerDoc { QuestionId = q.Id, Selected = picks });
        }
        var comment = (req.Comment ?? "").Trim();
        if (answers.Count == 0 && comment.Length == 0)
            return BadRequest(new { code = "sv.e.empty", message = "Pick at least one answer." });

        await surveys.AddResponseAsync(new SurveyResponseDoc
        {
            Id = ObjectId.GenerateNewId(),
            SurveyId = doc.Id,
            RestaurantId = storeId,
            At = DateTime.Now,
            Answers = answers,
            Comment = comment.Length == 0 ? null : comment[..Math.Min(500, comment.Length)],
            Source = string.IsNullOrWhiteSpace(req.Source) ? null : req.Source.Trim()[..Math.Min(60, req.Source.Trim().Length)],
        });
        return NoContent();
    }

    // ---------- helpers ----------

    /// <summary>A title, at least one question, every question at least two options.</summary>
    private static string? Validate(SaveSurveyRequest req, out List<SurveyQuestionDoc> questions)
    {
        questions = [];
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length > 120) return "sv.e.title";
        foreach (var q in req.Questions ?? [])
        {
            var text = (q.Text ?? "").Trim();
            var options = (q.Options ?? []).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Take(12).ToList();
            if (text.Length == 0 || options.Count < 2) continue;   // half-written rows are simply dropped
            questions.Add(new SurveyQuestionDoc
            {
                Id = string.IsNullOrWhiteSpace(q.Id) ? Guid.NewGuid().ToString("N")[..8] : q.Id,
                Text = text[..Math.Min(200, text.Length)],
                Options = options.Select(o => o[..Math.Min(80, o.Length)]).ToList(),
                Multi = q.Multi,
            });
        }
        if (questions.Count == 0) return "sv.e.questions";
        if (questions.Count > 20) questions = questions.Take(20).ToList();
        return null;
    }

    private static SurveyQuestionDto ToDto(SurveyQuestionDoc q) => new(q.Id, q.Text, q.Options, q.Multi);

    private static SurveyDto ToDto(SurveyDoc d, int responses) =>
        new(d.Id.ToString(), d.Title, d.IsActive, d.CreatedAt, d.Questions.Select(ToDto).ToList(), responses);
}
