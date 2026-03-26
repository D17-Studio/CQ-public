
/// <summary>
/// 饿汉无mono单例模板
/// </summary>
/// <typeparam name="T"></typeparam>
    public class SingletonHungry<T>  where T : SingletonHungry<T>, new()
    {
        
        private static T _instance = new T();
        
        public static T Instance { get { return _instance; } }
        
        
    }
