using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DotNetLib
{
    public class BaseObject : Il2CppObjectBase
    {
        private GameObject Manager;

        public BaseObject(IntPtr intPtr) : base(intPtr)
        {
            Init();
        }

        public BaseObject() : base(ClassInjector.DerivedConstructorPointer<BaseObject>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public void Init()
        {
            Console.WriteLine("Hooking");
            ClassInjector.RegisterTypeInIl2Cpp<CheatManager>();
            Manager = new GameObject("Dupont Trolling");
            Object.DontDestroyOnLoad(Manager);
            Manager.hideFlags |= HideFlags.HideAndDontSave;
            Manager.AddComponent<CheatManager>();
            Console.WriteLine("Hooked");
        }
    }
}