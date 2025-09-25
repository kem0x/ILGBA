using System.Diagnostics;
using ARM;

namespace Tests
{
    public class DecoderTests
    {
        public static void LinkBit()
        {
            uint b = 0xEA00002E; // B  +...
            uint bl = 0xEB00002E; // BL +...

            var db = Decoder.Instance.Decode(b);
            var dbl = Decoder.Instance.Decode(bl);

            Debug.Assert(db.LinkOrLoad == false);
            Debug.Assert(dbl.LinkOrLoad == true);
        }
    }
}