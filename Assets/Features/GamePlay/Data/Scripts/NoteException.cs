using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteException : Exception
{
    public NoteException() : base() { }
    
    public NoteException(string message) : base(message) { }
    
    public NoteException(string message, Exception innerException) 
        : base(message, innerException) { }
}