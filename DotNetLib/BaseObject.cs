using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = Il2CppSystem.Object;

namespace DotNetLib
{
    public class BaseObject : Object
    {
        private GameObject Manager;

        public BaseObject(IntPtr intPtr) : base(intPtr)
        {
        }

        public BaseObject() : base(ClassInjector.DerivedConstructorPointer<BaseObject>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public void Init()
        {
            /*
            ClassInjector.RegisterTypeInIl2Cpp<CheatManager>();
            Manager = new GameObject("DupontManager");
            UnityEngine.Object.DontDestroyOnLoad(Manager);
            Manager.hideFlags |= HideFlags.HideAndDontSave;
            Manager.AddComponent<CheatManager>();
            Console.WriteLine("Hooked");
            */
        }
    }
}