namespace DiscordBot.Options;

public sealed class AutoResponseOptions
{
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 45;

    public int CooldownSeconds { get; set; } = 180;

    public int MaxBufferedMessages { get; set; } = 20;

    public int ClassifierMaxTokens { get; set; } = 160;

    public string? ClassifierModel { get; set; }

    public string[] BotNameAliases { get; set; } =
    [
        "봇",
        "AI",
        "에이아이",
        "인공지능"
    ];

    public string ClassifierGuidelines { get; set; } = DefaultClassifierGuidelines;

    public const string DefaultClassifierGuidelines = """
should_respond=true 조건:
- 봇 이름/지칭(예: "봇", "AI", "에이아이", "인공지능")이 태그 없이 쓰였지만, 실제로 봇을 부르거나
  ("AI야", "봇 있어?") 봇에게 뭔가를 묻거나 시키는 의미여서 봇의 짧은 반응이 자연스러운 경우.
- 누군가 질문이나 도움 요청을 했고, 같은 묶음 안에서 사람이 충분히 답하지 않은 경우.
- 봇의 이전 응답에 대한 후속 반응처럼 보이는 경우.

should_respond=false 조건:
- 봇 이름/지칭 후보 단어가 등장하더라도, 봇을 부르거나 겨냥한 게 아니라 사람들끼리 "AI"라는
  기술/주제 자체를 화제로 대화하는 경우. 예: "AI를 이용해서 도구를 만들어봐", "요즘 AI 코딩 도구
  뭐 씀?" 같은 문장은 봇에게 말을 건 게 아니라 AI라는 단어가 화제에 등장한 것뿐이니 응답하지 마라.
  단어가 문장에 있다는 사실만으로 트리거하지 말고, 화자가 봇을 부르거나 겨냥했는지를 판단하라.
- 1:1 DM, 봇 태그/멘션, 이미 사람이 답한 대화, 잡담/감탄/밈처럼 끼어들 필요가 낮은 경우.
- 유튜브/웹 링크, 이미지, 첨부만 던지고 "이거 어때?"처럼 AI가 내용을 볼 수 없어 판단이 어려운 경우.
- 대화에 끼어드는 것이 어색하거나 비용 대비 가치가 낮은 경우.
""";
}
