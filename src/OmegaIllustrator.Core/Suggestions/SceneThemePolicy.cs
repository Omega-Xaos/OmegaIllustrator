using System.Text;

namespace OmegaIllustrator.Core.Suggestions;

public sealed record SceneCompositionIntent(string Activity, string Mood, string Organization, string Grouping);

public static class SceneThemePolicy
{
    private static readonly string[] FinancialCues = ["финанс", "деньг", "бюджет", "инвест", "доход", "бухгалтер", "financial", "budget", "investment"];
    private static readonly string[] LearningCues = ["учеб", "изуч", "обуч", "школ", "урок", "экзам", "материал", "знан", "learning", "study", "education", "course"];
    private static readonly string[] ProblemSolvingCues = ["логик", "решен", "задач", "анализ", "аналит", "сложн", "головолом", "problem", "solve", "analysis"];
    private static readonly string[] DigitalWorkCues = ["цифров", "онлайн", "связ", "сообщ", "звон", "digital", "online", "communication"];
    private static readonly string[] ProjectCues = ["проект", "работ", "документ", "отчет", "отчёт", "задач", "счет", "счёт"];
    private static readonly string[] ProjectWorkCues = [.. ProjectCues, .. LearningCues, "продуктив", "бизнес", "project", "work", "business"];
    private static readonly string[] SeriousWorkCues = ["серьез", "серьёз", "делов", "бизнес", "ответствен", "сосредоточ", "продуктив", "productive", "productivity", "serious", "focused"];
    private static readonly string[] RestCues = ["отдых", "расслаб", "перерыв", "пауза", "гармони", "умиротвор", "спокой", "тиш", "медит", "rest", "relax", "break", "harmony", "serenity", "serene", "calm", "tranquil", "quiet"];
    private static readonly string[] CozyCues = ["уют", "тепл", "домаш", "уютн", "cozy", "warm", "comfortable"];
    private static readonly string[] NatureCues = ["природ", "сад", "цвет", "букет", "растен", "nature", "garden", "flower"];
    private static readonly string[] LightCues = ["свет", "освещ", "ламп", "вечер", "ноч", "light", "lighting", "evening", "night"];
    private static readonly string[] ProcrastinationCues = ["прокраст", "откладыв", "отвлека", "distraction", "procrast"];
    private static readonly string[] FinancialDocumentCues = ["проект", "документ", "отчет", "отчёт", "смет", "invoice", "report", "document"];
    private static readonly string[] OrganizedWorkCues = ["дисциплин", "организован", "порядок", "структурирован", "собран", "планомер", "disciplined", "organized", "orderly"];
    private static readonly string[] DisorderCues = ["хаос", "беспоряд", "суматош", "бардак", "разбросан", "небреж", "chaos", "chaotic", "messy", "disorder", "scatter"];
    private static readonly string[] CompactCues = ["компакт", "плотн", "близк", "сгруппиров", "compact", "close", "tight"];
    private static readonly string[] AiryCues = ["воздушн", "просторн", "свободн", "разреж", "airy", "spacious", "open"];
    private static readonly string[] CreativeCues = ["творч", "креатив", "мозговой штурм", "дизайн", "creative", "creativity", "brainstorm", "design"];
    private static readonly string[] EnergeticCues = ["энергич", "насыщен", "динамич", "активн", "energetic", "dynamic", "active", "vibrant"];

    private static readonly IReadOnlyDictionary<string, string[]> ExplicitObjectCues = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["headphones"] = ["наушник", "headphone", "музык", "music", "слуш", "listen"],
        ["flowers"] = ["цвет", "букет", "растен", "flower", "plant"],
        ["book"] = ["книг", "учеб", "чтен", "чит", "консп", "book", "read", "study"],
        ["puzzle"] = ["пазл", "головолом", "puzzle"],
        ["phone"] = ["телефон", "смартфон", "phone"],
        ["lamp"] = ["ламп", "светильник", "освещен", "lamp", "light"],
        ["folder"] = ["папк", "документ", "отчет", "отчёт", "folder", "document", "report"],
        ["wallet"] = ["кошелек", "кошелёк", "wallet"],
        ["coin"] = ["монет", "деньг", "бюджет", "финанс", "coin", "money", "budget", "finance"],
        ["pen"] = ["ручк", "карандаш", "запис", "заметк", "pen", "note", "write"]
    };

    private static readonly IReadOnlyDictionary<string, string> ObjectMeaningMatrix = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["book"] = "learning, reading, reference, and understanding; a large surface and a natural anchor for a study scene. Pen or puzzle may rest on it; a phone may visibly peek from behind only when the story supports that relation",
        ["phone"] = "communication, digital work, reminders, or distraction; a small but recognizable device. Keep it beside the task, or let it peek 25-45 percent from behind a book/folder when the prompt implies interruption or organization",
        ["puzzle"] = "problem solving, analysis, and joining parts into a solution; a task object that belongs on a relevant book/folder or close to the work surface, never as unexplained decoration",
        ["coin"] = "money, value, budgeting, and financial learning; a small concrete cue. Pair it with a wallet or financial folder, not with unrelated comfort objects",
        ["headphones"] = "music, concentration, or personal rest; pair with a phone for listening or with a book for a deliberate reading break. Do not use as generic decoration or a financial-work cue",
        ["lamp"] = "local task light; tall support object placed behind or to one side of the work area. Use only when long focus, evening, warmth, or lighting is relevant; never make it the foreground subject by default",
        ["flowers"] = "nature, care, and comfort; an optional perimeter accent for an explicitly calm, cozy, or nature-led scene. They do not signify seriousness, finance, or productivity by themselves",
        ["wallet"] = "personal money and financial organization; a small support object beside a budget/document area, naturally associated with a coin",
        ["folder"] = "documents, organization, project structure, and records; a stable work surface and a strong anchor for business or administrative scenes",
        ["pen"] = "writing, notes, decisions, and calculations; a small slender tool that rests diagonally on a book/folder or stays close to its writing area"
    };

    private static readonly IReadOnlyDictionary<string, string> ObjectVisualRegions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["book"] = "Wide open two-page spread with layered page edges/cover and a ribbon bookmark. The broad page planes are the readable work surface; the narrow ribbon and page-edge bands are the smaller color accents. Keep the full spread visible.",
        ["phone"] = "One slim tilted smartphone body with a framed screen, small top speaker/camera details, and a lower home-button/device edge. The screen is part of the phone SVG, not an extra loose object. Keep the silhouette readable and do not cover the screen as if editing its internal graphics.",
        ["puzzle"] = "Two interlocking jigsaw pieces with separate fill regions and a shared dark contour. Treat the pair as one problem-solving object; place it on a book/folder surface or close to that work area, and keep both pieces identifiable.",
        ["coin"] = "Small round coin with concentric rim/face details and a tiny central emblem; the dark off-center shape is its cast shadow/contour, not another object. Keep the face and rim visually distinct.",
        ["headphones"] = "Over-ear headset with one curved headband, two padded ear cups, and the inner cup rings. Keep both cups and the bridge clear; it is a listening/rest cue, not a generic productivity prop.",
        ["lamp"] = "Desk lamp composed of a flat base, upright support/curved arm, neck, polygonal shade, and visible inner light area. Keep it upright and behind/aside the work surface so its shade can point toward the task without becoming the foreground focal object.",
        ["flowers"] = "A small vase with stems and a bouquet of separate flower heads/petals. Preserve the silhouette and the petal/stem/vase contrast; use it at the outside edge as an optional nature/cozy accent.",
        ["wallet"] = "Compact folded wallet with a broad body, visible flap/fastener, stitched or layered edge, and a narrow opening. Keep the flap and closure readable; pair it with a coin or financial documents rather than placing it randomly.",
        ["folder"] = "Broad document folder with a tab/back sheet, visible paper edge, and front cover plane. It is a stable project/record surface; keep the tab and layered sheets visible when another object rests on it.",
        ["pen"] = "Long narrow writing instrument with a barrel, cap/clip, grip rings, and pointed nib. Preserve the single uniform aspect ratio; place it diagonally on a book/folder or close to the active writing area, never across an unrelated object."
    };

    private static readonly HashSet<(string Source, string Target, string Type)> PhysicalRelations =
    [
        ("pen", "book", "rests_on"), ("pen", "folder", "rests_on"),
        ("puzzle", "book", "rests_on"), ("puzzle", "folder", "rests_on"),
        ("coin", "wallet", "rests_on"), ("coin", "folder", "rests_on"),
        ("phone", "book", "rests_on"), ("phone", "folder", "rests_on"),
        ("book", "folder", "rests_on"), ("folder", "book", "rests_on"),
        ("puzzle", "book", "inside"), ("puzzle", "folder", "inside"), ("coin", "wallet", "inside"), ("pen", "folder", "inside"),
        ("pen", "book", "overlaps"), ("pen", "folder", "overlaps"),
        ("puzzle", "book", "overlaps"), ("puzzle", "folder", "overlaps"),
        ("coin", "wallet", "overlaps"), ("coin", "folder", "overlaps")
    ];

    public static string ObjectDecisionMatrixPrompt => string.Join(Environment.NewLine,
        ObjectMeaningMatrix.Select(pair => $"{pair.Key}: {pair.Value} Visible regions: {ObjectVisualRegions[pair.Key]}"));

    public static string VisualRegionsFor(string concept) =>
        ObjectVisualRegions.GetValueOrDefault(concept, "Use only the supplied source SVG silhouette; do not infer or invent internal parts.");

    public static string RelationshipDecisionMatrixPrompt =>
        "Relation direction: for rests_on/inside/behind, source is the item being positioned and target is its support or anchor; for illuminates, source is the lamp and target is the work surface; supports means source is the support surface and target is the item on it. " +
        "Physical matrix: pen rests on book/folder; puzzle rests on book/folder (use inside only if a plausible open/container relation is visibly supported); coin rests on or sits partly inside wallet, or rests on financial folder; phone rests on or peeks from behind book/folder with 25-45 percent of its body visible; wallet sits beside a financial folder; book and folder are separate surfaces and are not stacked by default. " +
        "Context links: headphones pair with a phone for listening or with a book for a deliberate break; flowers stay on the outer edge of a calm/cozy/nature-led scene; lamp illuminates from behind or one side and is never a default foreground focal object. " +
        "Use only links that explain what the objects are doing together. A relation is a physical promise: source is the movable/top object, target is the support for rests_on/inside/behind; do not make a support itself rest on one of its overlays. Only use relation pairs permitted by the supplied composition policy. Do not claim a 3D physical placement that the flat SVG silhouettes cannot communicate, and do not use beside as a substitute for semantic relevance.";

    public static string CompositionModelPrompt =>
        "Interpret four independent axes: primary activity, emotional tone, spatial organization, and grouping density. Supported domains include learning/study, project/business, finance, rest/recovery, problem-solving/creative work, digital communication, procrastination/attention conflict, and explicitly mixed tasks. Decide the primary activity first; mood modifies it unless the user makes mood the subject. For work plus comfort, keep work as the story and comfort as a small secondary cue. " +
        "Use these domain rules as a semantic matrix, never as fixed bundles: study—book is a knowledge surface, pen belongs on/near its writing area, puzzle is relevant only for exercises, phone is peripheral unless digital study is requested, lamp only for long/evening/light-focused work; project/business—folder or book anchors documents, pen is on the active surface, phone is nearby/partly hidden only when the story supports it, lamp is side/rear support; finance—folder/document with pen, wallet, and coin form a connected money-work group, phone only for an explicit digital task, flowers/headphones are excluded unless directly requested and relevant; rest—choose book/headphones/flowers according to the actual activity, create a cozy connected vignette, do not insert finance tools or a lamp by reflex; problem-solving—puzzle is on/near the book/folder it belongs to; digital communication—phone is the anchor and headphones pair only when listening is implied; procrastination—show a readable tension between task and distraction; chaos—vary spacing/angles/clusters while retaining a recognizable anchor and sensible interactions. " +
        "For each object, decide its unique narrative function, task relevance, importance, complexity, relative scale cue, depth, relation, and palette contribution. Pick one clear focal object, useful task supports, and optional atmosphere. Never select an object merely to reach a count. A fourth or later object must add a distinct role. Keep the cast and spatial pattern varied across repeated prompts: do not repeat the same anchor, object set, and relation graph while only changing the palette."
        ;

    public static IReadOnlyList<string> NormalizeTerms(string description) =>
        description.Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant()
            .Replace('ё', 'е')
            .Split([' ', '\t', '\r', '\n', ',', ';', '.', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsFinancialIntent(IEnumerable<string> normalizedTerms) =>
        normalizedTerms.Any(term => ContainsAny(term, FinancialCues));

    public static bool IsRestIntent(IEnumerable<string> normalizedTerms) =>
        !IsFinancialIntent(normalizedTerms) && !IsProjectWorkIntent(normalizedTerms) && normalizedTerms.Any(term => ContainsAny(term, RestCues));

    public static bool IsProjectWorkIntent(IEnumerable<string> normalizedTerms) =>
        normalizedTerms.Any(term => ContainsAny(term, ProjectWorkCues) || ContainsAny(term, SeriousWorkCues));

    public static bool IsOrganizedWorkIntent(IEnumerable<string> normalizedTerms) =>
        normalizedTerms.Any(term => ContainsAny(term, OrganizedWorkCues));

    public static SceneCompositionIntent InferCompositionIntent(string description) =>
        InferCompositionIntent(NormalizeTerms(description));

    public static SceneCompositionIntent InferCompositionIntent(IEnumerable<string> normalizedTerms)
    {
        var terms = normalizedTerms as IReadOnlyCollection<string> ?? normalizedTerms.ToArray();
        var financial = IsFinancialIntent(terms);
        var learning = ContainsAny(terms, LearningCues);
        var problemSolving = ContainsAny(terms, ProblemSolvingCues);
        var digital = ContainsAny(terms, DigitalWorkCues);
        var procrastination = IsProcrastinationIntent(terms);
        var rest = ContainsAny(terms, RestCues);
        var work = IsProjectWorkIntent(terms);
        var creative = ContainsAny(terms, CreativeCues);
        var cozy = ContainsAny(terms, CozyCues);
        var serious = ContainsAny(terms, SeriousWorkCues);
        var energetic = ContainsAny(terms, EnergeticCues);

        var activity = procrastination ? "procrastination_attention_conflict"
            : financial && learning ? "financial_learning"
            : financial ? "finance"
            : learning && problemSolving ? "learning_problem_solving"
            : learning ? "learning_study"
            : problemSolving ? "problem_solving"
            : digital ? "digital_communication"
            : rest && !work ? "rest_recovery"
            : work ? "project_business_work"
            : creative ? "creative_work"
            : "general_scene";

        var mood = cozy ? "warm_cozy"
            : rest ? "calm_rest"
            : serious ? "serious_focused"
            : energetic ? "energetic"
            : procrastination ? "conflicted_attention"
            : "neutral_or_user_defined";

        var organization = ContainsAny(terms, DisorderCues) ? "irregular_but_readable"
            : IsOrganizedWorkIntent(terms) ? "orderly_connected_work_zone"
            : "balanced_natural_grouping";

        var grouping = ContainsAny(terms, CompactCues) ? "compact_group"
            : ContainsAny(terms, AiryCues) ? "airy_group"
            : "balanced_spacing";

        return new SceneCompositionIntent(activity, mood, organization, grouping);
    }

    public static SceneRelationship? RecommendRelationship(
        string concept,
        string focalConcept,
        IReadOnlySet<string> availableConcepts,
        SceneCompositionIntent intent)
    {
        if (concept == focalConcept || !availableConcepts.Contains(concept) || !availableConcepts.Contains(focalConcept)) return null;

        var workSurface = focalConcept is "book" or "folder" ? focalConcept
            : availableConcepts.Contains("folder") ? "folder"
            : availableConcepts.Contains("book") ? "book"
            : focalConcept;

        if (concept == "pen" && workSurface is "book" or "folder") return new(concept, workSurface, "rests_on", 0.86);
        if (concept == "puzzle" && workSurface is "book" or "folder") return new(concept, workSurface, "rests_on", 0.82);
        if (concept == "coin")
        {
            var support = availableConcepts.Contains("wallet") ? "wallet" : workSurface is "folder" or "book" ? workSurface : null;
            return support is null ? null : new(concept, support, "rests_on", 0.78);
        }
        if (concept == "phone")
        {
            var target = focalConcept is "book" or "folder" or "phone" ? focalConcept : workSurface;
            var relation = intent.Activity == "procrastination_attention_conflict" ? "beside"
                : intent.Organization == "orderly_connected_work_zone" && (target is "book" or "folder") ? "behind"
                : "beside";
            return new(concept, target, relation, 0.7);
        }
        if (concept == "headphones")
        {
            var target = availableConcepts.Contains("phone") ? "phone" : availableConcepts.Contains("book") ? "book" : focalConcept;
            var relation = target is "book" or "phone" ? "pairs_with" : "beside";
            return new(concept, target, relation, 0.72);
        }
        if (concept == "flowers") return new(concept, focalConcept, "beside", 0.62);
        if (concept == "lamp") return new(concept, workSurface, "illuminates", 0.66);
        if (concept == "wallet" && workSurface is "folder" or "book") return new(concept, workSurface, "beside", 0.72);
        if (concept == "book" && focalConcept == "folder" || concept == "folder" && focalConcept == "book")
            return new(concept, focalConcept, "beside", 0.7);

        return new(concept, focalConcept, "beside", 0.58);
    }

    public static bool IsProcrastinationIntent(IEnumerable<string> normalizedTerms) =>
        normalizedTerms.Any(term => ContainsAny(term, ProcrastinationCues));

    public static bool IsConceptAllowed(string concept, IEnumerable<string> normalizedTerms)
    {
        var terms = normalizedTerms as IReadOnlyCollection<string> ?? normalizedTerms.ToArray();
        var explicitRequest = ExplicitObjectCues.TryGetValue(concept, out var cues) && terms.Any(term => ContainsAny(term, cues));
        if (explicitRequest) return true;

        var learning = ContainsAny(terms, LearningCues);
        var problemSolving = ContainsAny(terms, ProblemSolvingCues);
        var cozy = ContainsAny(terms, CozyCues);
        var finance = IsFinancialIntent(terms);
        var rest = IsRestIntent(terms);
        var work = IsProjectWorkIntent(terms);
        var restMood = ContainsAny(terms, RestCues);
        var procrastination = IsProcrastinationIntent(terms);

        var demandingTask = ContainsAny(terms, SeriousWorkCues) || problemSolving || learning;
        if (concept == "lamp") return !procrastination && (cozy || ContainsAny(terms, LightCues) || work && (restMood || demandingTask));
        if (concept == "flowers") return !work && (rest || cozy || ContainsAny(terms, NatureCues));
        if (concept == "headphones") return !finance && (!work || restMood || procrastination);

        if (finance)
        {
            if (concept == "book") return learning;
            if (concept == "puzzle") return learning || problemSolving;
            if (concept == "phone") return work || ContainsAny(terms, DigitalWorkCues);
            return true;
        }

        if (rest)
        {
            if (concept is "coin" or "wallet") return false;
            if (concept == "folder") return terms.Any(term => ContainsAny(term, ProjectCues));
            if (concept == "puzzle") return learning || problemSolving;
            if (concept == "phone") return ContainsAny(terms, DigitalWorkCues);
            return true;
        }

        if (work)
        {
            if (concept == "coin") return false;
            if (concept == "wallet") return restMood || ContainsAny(terms, SeriousWorkCues);
            return true;
        }

        return true;
    }

    public static bool AllowsRelationship(string source, string target, string type) => type switch
    {
        "rests_on" or "inside" or "overlaps" => PhysicalRelations.Contains((source, target, type)),
        "behind" => source is "flowers" or "headphones" or "phone" && target is "book" or "folder",
        "illuminates" => source == "lamp" && target is "book" or "folder" or "phone" or "puzzle" or "pen" or "wallet" or "coin",
        "connects_to" => (source is "puzzle" or "phone") && (target is "book" or "folder" or "phone" or "puzzle" or "coin"),
        "supports" => (source is "book" or "folder" or "wallet") && (target is "pen" or "puzzle" or "coin" or "phone"),
        "beside" => AreContextuallyRelated(source, target),
        "pairs_with" => AreNaturalPair(source, target),
        _ => false
    };

    public static IReadOnlySet<string> PreferredAccentConcepts(
        IEnumerable<string> normalizedTerms,
        IEnumerable<string> availableConcepts,
        string? focalConcept = null)
    {
        var terms = normalizedTerms as IReadOnlyCollection<string> ?? normalizedTerms.ToArray();
        var available = availableConcepts.ToHashSet(StringComparer.Ordinal);
        var accents = new HashSet<string>(StringComparer.Ordinal);
        string[] preferred = IsFinancialIntent(terms)
            ? ContainsAny(terms, LearningCues) ? ["book", "coin"] : ["folder", "wallet", "coin"]
            : IsRestIntent(terms)
                ? ["book", "headphones", "flowers"]
                : IsProjectWorkIntent(terms)
                    ? ContainsAny(terms, RestCues)
                        ? ["phone", "lamp", "folder", "book", "wallet"]
                        : ["book", "folder", "puzzle", "phone"]
                    : [];

        foreach (var concept in preferred.Take(2))
            if (available.Contains(concept)) accents.Add(concept);
        if (focalConcept is not null && available.Contains(focalConcept)) accents.Add(focalConcept);
        return accents;
    }

    public static bool AllowsInFinancialScene(string concept, IReadOnlyCollection<string> normalizedTerms) =>
        !IsFinancialIntent(normalizedTerms) || IsConceptAllowed(concept, normalizedTerms);

    public static string? PreferredFinancialFocus(IEnumerable<string> normalizedTerms, IEnumerable<string> availableConcepts)
    {
        var terms = normalizedTerms as IReadOnlyCollection<string> ?? normalizedTerms.ToArray();
        if (!IsFinancialIntent(terms)) return null;

        var concepts = availableConcepts.ToHashSet(StringComparer.Ordinal);
        if (ContainsAny(terms, LearningCues) && concepts.Contains("book")) return "book";
        if (terms.Any(term => term.Contains("монет", StringComparison.Ordinal) || term.Contains("coin", StringComparison.Ordinal)) && concepts.Contains("coin"))
            return "coin";
        if (terms.Any(term => term.Contains("кошелек", StringComparison.Ordinal) || term.Contains("кошелёк", StringComparison.Ordinal) || term.Contains("wallet", StringComparison.Ordinal)) && concepts.Contains("wallet"))
            return "wallet";
        if (ContainsAny(terms, FinancialDocumentCues) && concepts.Contains("folder"))
            return "folder";
        return null;
    }

    public static string? PreferredRestFocus(IEnumerable<string> normalizedTerms, IEnumerable<string> availableConcepts)
    {
        var terms = normalizedTerms as IReadOnlyCollection<string> ?? normalizedTerms.ToArray();
        if (!IsRestIntent(terms)) return null;

        var concepts = availableConcepts.ToHashSet(StringComparer.Ordinal);
        if (terms.Any(term => ContainsAny(term, ExplicitObjectCues["headphones"])) && concepts.Contains("headphones"))
            return "headphones";
        if (ContainsAny(terms, LearningCues) && concepts.Contains("book")) return "book";
        if (terms.Any(term => ContainsAny(term, ExplicitObjectCues["flowers"])) && concepts.Contains("flowers")) return "flowers";
        return null;
    }

    private static bool AreContextuallyRelated(string first, string second)
    {
        var pair = new HashSet<string>([first, second], StringComparer.Ordinal);
        return pair.SetEquals(["book", "pen"]) || pair.SetEquals(["book", "puzzle"]) || pair.SetEquals(["book", "phone"]) ||
            pair.SetEquals(["book", "headphones"]) || pair.SetEquals(["book", "flowers"]) || pair.SetEquals(["book", "lamp"]) ||
            pair.SetEquals(["book", "folder"]) || pair.SetEquals(["book", "coin"]) || pair.SetEquals(["book", "wallet"]) ||
            pair.SetEquals(["folder", "pen"]) || pair.SetEquals(["folder", "puzzle"]) || pair.SetEquals(["folder", "phone"]) ||
            pair.SetEquals(["folder", "wallet"]) || pair.SetEquals(["folder", "coin"]) || pair.SetEquals(["folder", "lamp"]) ||
            pair.SetEquals(["phone", "headphones"]) || pair.SetEquals(["phone", "pen"]) || pair.SetEquals(["phone", "puzzle"]) ||
            pair.SetEquals(["phone", "wallet"]) || pair.SetEquals(["headphones", "flowers"]) || pair.SetEquals(["headphones", "lamp"]) ||
            pair.SetEquals(["wallet", "coin"]) || pair.SetEquals(["wallet", "phone"]) || pair.SetEquals(["puzzle", "pen"]);
    }

    private static bool AreNaturalPair(string first, string second)
    {
        var pair = new HashSet<string>([first, second], StringComparer.Ordinal);
        return pair.SetEquals(["headphones", "phone"]) || pair.SetEquals(["headphones", "book"]);
    }

    private static bool ContainsAny(IEnumerable<string> terms, IEnumerable<string> cues) =>
        terms.Any(term => ContainsAny(term, cues));

    private static bool ContainsAny(string term, IEnumerable<string> cues) =>
        cues.Any(cue => term.Contains(cue, StringComparison.Ordinal));
}
