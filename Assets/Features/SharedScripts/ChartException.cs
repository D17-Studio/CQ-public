using System;

namespace CQMusicGame.Shared
{
    public class ChartException : Exception
    {
        public ChartException() : base() { }
    
        public ChartException(string message) : base(message) { }
    
        public ChartException(string message, Exception innerException) 
            : base(message, innerException) { }
    }
}

