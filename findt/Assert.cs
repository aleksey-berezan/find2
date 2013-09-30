using System;

namespace findt
{
    internal static class Assert
    {

        public static void Critical(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        public static void Soft(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertException(message);
            }
        }
    }
}