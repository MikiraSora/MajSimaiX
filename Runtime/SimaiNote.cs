using System.Runtime.InteropServices;

namespace MajSimai
{
    public class SimaiNote
    {
        public SimaiNoteType Type { get; set; }
        public int StartPosition { get; set; } = 1; //键位（1-8）

        public double HoldTime { get; set; }
        public bool IsBreak { get; set; }
        public bool IsEx { get; set; }
        public bool IsFakeRotate { get; set; }
        public bool IsForceStar { get; set; }
        public bool IsHanabi { get; set; }
        public bool IsSlideBreak { get; set; }
        public bool IsSlideNoHead { get; set; }
        public bool IsMine { get; set; } //炸弹音符
        public bool IsMineSlide { get; set; }
        public int SoflanGroup { get; set; } = 0; //变速分组
        private int? _slideSoflanGroup;
        public int SlideSoflanGroup //Slide轨迹变速分组；未单独指定时继承星头分组
        {
            get => _slideSoflanGroup ?? SoflanGroup;
            set => _slideSoflanGroup = value;
        }
        public bool IsFixedSoflan { get; set; }
        public bool HasFixedSoflanSpeed { get; set; }
        public float FixedSoflanSpeed { get; set; } = DefaultFixedSoflanSpeed;

        public const float DefaultFixedSoflanSpeed = 600f;

        public string RawContent { get; set; } //used for star explain

        public double SlideStartTime { get; set; }
        public double SlideTime { get; set; }
        public char TouchArea { get; set; } = ' ';

#if NET7_0_OR_GREATER
        internal unsafe MajSimai.Unmanaged.UnmanagedSimaiNote ToUnmanaged()
        {
            var rawContentPtr = (char*)null;
            if(!string.IsNullOrEmpty(RawContent))
            {
                rawContentPtr = (char*)Marshal.StringToHGlobalAnsi(RawContent);
            }

            return new()
            {
                type = Type,
                startPosition = StartPosition,
                holdTime = HoldTime,
                slideTime = SlideTime,
                slideStartTime = SlideStartTime,

                isBreak = IsBreak,
                isFakeRotate = IsFakeRotate,
                isForceStar = IsForceStar,
                isHanabi = IsHanabi,
                isSlideBreak = IsSlideBreak,
                isEx = IsEx,
                isMine = IsMine,
                isMineSlide = IsMineSlide,
                isSlideNoHead = IsSlideNoHead,
                soflanGroup = SoflanGroup,
                slideSoflanGroup = SlideSoflanGroup,
                touchArea = TouchArea,

                rawContent = rawContentPtr,
                rawContentLen = RawContent.Length
            };
        }
#endif
    }
}
