namespace TinyFs.Interop.Helpers
{
    public static class OpHelper
    {
        public static int DivWithRoundUp(int first, int second)
        {
            return (first + second - 1) / second;
        }
    }
}