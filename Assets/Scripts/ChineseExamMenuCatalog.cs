using System; // 추첨용 난수 생성기를 사용한다.
using System.Collections.Generic; // 외부에서 메뉴 목록을 바꾸지 못하도록 읽기 전용 목록을 제공한다.

/// <summary>중식 20개 메뉴 중 조리가 구현된 메뉴에서만 중복 없이 두 과제를 뽑는다.</summary>
public static class ChineseExamMenuCatalog
{
    private static readonly string[] names = // 사용자 요청의 20개 출제 메뉴를 등록한다.
    {
        "오징어냉채", "해파리냉채", "탕수육", "깐풍기", "탕수생선살",
        "난자완스", "홍쇼두부", "마파두부", "새우케첩볶음", "양장피잡채",
        "고추잡채", "채소볶음", "라조기", "부추잡채", "경장육사",
        "유니짜장면", "울면", "새우볶음밥", "빠스옥수수", "빠스고구마"
    };
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(names); // 목록 수정으로 추첨 대상이 달라지는 것을 막는다.

    private static readonly string[] implemented = // 조리까지 구현된 메뉴만 추첨 대상이다. 새 메뉴를 완성하면 여기에 이름을 추가한다.
    {
        "탕수육", "마파두부"
    };
    public static IReadOnlyList<string> Implemented { get; } = Array.AsReadOnly(implemented); // 화면에 추첨 대상 수를 보여줄 때 쓴다.

    public static bool IsImplemented(string dish) => Array.IndexOf(implemented, dish) >= 0; // 조리 가능한 메뉴인지 확인한다.

    public static string[] DrawPair(Random random) // 난수 생성기를 주입해 같은 시드로 검사할 수 있게 한다.
    {
        if (random == null) throw new ArgumentNullException(nameof(random)); // 잘못된 호출을 즉시 알린다.
        int first = random.Next(implemented.Length); // 첫 메뉴는 구현된 메뉴 중 같은 확률로 선택한다.
        int second = random.Next(implemented.Length - 1); // 두 번째 메뉴는 첫 메뉴를 뺀 나머지에서 선택한다.
        if (second >= first) second++; // 이미 뽑힌 첫 메뉴의 위치를 건너뛴다.
        return new[] { implemented[first], implemented[second] }; // 추첨 결과 두 개를 새 배열로 돌려준다.
    }
}
