using System;

public interface IPoolable
{
    void SetReturnCallback(Action onReturn);
}
