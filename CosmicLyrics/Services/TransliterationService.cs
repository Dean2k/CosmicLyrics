using System.Text;
using hyjiacan.py4n;

namespace CosmicLyrics.Services;

public static class TransliterationService
{
    private static readonly PinyinFormat _pinyinFormat =
        PinyinFormat.WITH_TONE_MARK | PinyinFormat.LOWERCASE | PinyinFormat.WITH_U_UNICODE;

    // Detect if text contains CJK characters
    public static bool ContainsCjk(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
        {
            if (IsCjk(c)) return true;
        }
        return false;
    }

    public static string Romanize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length * 3);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsChinese(c))
            {
                sb.Append(ChineseToPinyin(c));
                sb.Append(' '); // space between Chinese characters
            }
            else if (IsHiragana(c))
            {
                sb.Append(HiraganaToRomaji(c));
            }
            else if (IsKatakana(c))
            {
                sb.Append(KatakanaToRomaji(c));
            }
            else if (IsHangul(c))
            {
                sb.Append(HangulToRomaja(c));
                sb.Append(' '); // space between Korean syllables
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsCjk(char c)
        => IsChinese(c) || IsHiragana(c) || IsKatakana(c) || IsHangul(c);

    private static bool IsChinese(char c)
        => c is >= '一' and <= '鿿'
            or >= '㐀' and <= '䶿'
            or >= '⼀' and <= '⿟'
            or >= '豈' and <= '﫿';

    private static bool IsHiragana(char c)
        => c is >= '぀' and <= 'ゟ';

    private static bool IsKatakana(char c)
        => c is >= '゠' and <= 'ヿ' or >= '･' and <= 'ﾟ';

    private static bool IsHangul(char c)
        => c is >= '가' and <= '힯'
            or >= 'ᄀ' and <= 'ᇿ'
            or >= '㄰' and <= '㆏';

    // ============ Chinese ============
    private static string ChineseToPinyin(char c)
    {
        try
        {
            var readings = Pinyin4Net.GetPinyin(c, _pinyinFormat);
            if (readings != null && readings.Length > 0)
                return readings[0];
            return c.ToString();
        }
        catch
        {
            return c.ToString();
        }
    }

    // ============ Japanese Hiragana ============
    private static readonly Dictionary<char, string> HiraganaMap = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "wo", ['ん'] = "n",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['だ'] = "da", ['ぢ'] = "ji", ['づ'] = "zu", ['で'] = "de", ['ど'] = "do",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ゃ'] = "ya", ['ゅ'] = "yu", ['ょ'] = "yo",
        ['っ'] = "tsu",
        ['ゔ'] = "vu", ['ゕ'] = "ka", ['ゖ'] = "ke",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
    };

    private static string HiraganaToRomaji(char c)
        => HiraganaMap.TryGetValue(c, out var r) ? r : c.ToString();

    // ============ Japanese Katakana ============
    private static readonly Dictionary<char, string> KatakanaMap = new()
    {
        ['ア'] = "a", ['イ'] = "i", ['ウ'] = "u", ['エ'] = "e", ['オ'] = "o",
        ['カ'] = "ka", ['キ'] = "ki", ['ク'] = "ku", ['ケ'] = "ke", ['コ'] = "ko",
        ['サ'] = "sa", ['シ'] = "shi", ['ス'] = "su", ['セ'] = "se", ['ソ'] = "so",
        ['タ'] = "ta", ['チ'] = "chi", ['ツ'] = "tsu", ['テ'] = "te", ['ト'] = "to",
        ['ナ'] = "na", ['ニ'] = "ni", ['ヌ'] = "nu", ['ネ'] = "ne", ['ノ'] = "no",
        ['ハ'] = "ha", ['ヒ'] = "hi", ['フ'] = "fu", ['ヘ'] = "he", ['ホ'] = "ho",
        ['マ'] = "ma", ['ミ'] = "mi", ['ム'] = "mu", ['メ'] = "me", ['モ'] = "mo",
        ['ヤ'] = "ya", ['ユ'] = "yu", ['ヨ'] = "yo",
        ['ラ'] = "ra", ['リ'] = "ri", ['ル'] = "ru", ['レ'] = "re", ['ロ'] = "ro",
        ['ワ'] = "wa", ['ヲ'] = "wo", ['ン'] = "n",
        ['ガ'] = "ga", ['ギ'] = "gi", ['グ'] = "gu", ['ゲ'] = "ge", ['ゴ'] = "go",
        ['ザ'] = "za", ['ジ'] = "ji", ['ズ'] = "zu", ['ゼ'] = "ze", ['ゾ'] = "zo",
        ['ダ'] = "da", ['ヂ'] = "ji", ['ヅ'] = "zu", ['デ'] = "de", ['ド'] = "do",
        ['バ'] = "ba", ['ビ'] = "bi", ['ブ'] = "bu", ['ベ'] = "be", ['ボ'] = "bo",
        ['パ'] = "pa", ['ピ'] = "pi", ['プ'] = "pu", ['ペ'] = "pe", ['ポ'] = "po",
        ['ャ'] = "ya", ['ュ'] = "yu", ['ョ'] = "yo",
        ['ッ'] = "tsu", ['ァ'] = "a", ['ィ'] = "i", ['ゥ'] = "u", ['ェ'] = "e", ['ォ'] = "o",
        ['ヴ'] = "vu", ['ヵ'] = "ka", ['ヶ'] = "ke",
        ['ヷ'] = "wa", ['ヸ'] = "wi", ['ヹ'] = "we", ['ヺ'] = "wo",
    };

    private static string KatakanaToRomaji(char c)
        => KatakanaMap.TryGetValue(c, out var r) ? r : c.ToString();

    // ============ Korean Hangul ============
    private static readonly string[] Choseong =
    {
        "g","gg","n","d","dd","r","m","b","bb","s","ss","","j","jj","ch","k","t","p","h"
    };
    private static readonly string[] Jungseong =
    {
        "a","ae","ya","yae","eo","e","yeo","ye","o","wa","wae","oe","yo","u","wo","we","wi","yu","eu","ui","i"
    };
    private static readonly string[] Jongseong =
    {
        "","g","gg","gs","n","nj","nh","d","l","lg","lm","lb","ls","lt","lp","lh","m","b","bs","s","ss","ng","j","ch","k","t","p","h"
    };

    private static string HangulToRomaja(char c)
    {
        if (c is < '가' or > '힣') return c.ToString();

        int code = c - '가';
        int jong = code % 28;
        int jung = ((code - jong) / 28) % 21;
        int cho = ((code - jong) / 28) / 21;

        var sb = new StringBuilder();
        sb.Append(Choseong[cho]);
        sb.Append(Jungseong[jung]);
        sb.Append(Jongseong[jong]);
        return sb.ToString();
    }
}
