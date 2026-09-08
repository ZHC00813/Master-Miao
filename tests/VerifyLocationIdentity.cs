using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWBodyOrganizer;

// Contract tests with explicit COM-interface doubles, NOT live SolidWorks evidence.
internal static class VerifyLocationIdentity
{
    private sealed class Proxy : RealProxy
    {
        private readonly Func<IMethodCallMessage, object[], object> dispatch;
        internal Proxy(Type type, Func<IMethodCallMessage, object[], object> dispatch) : base(type) { this.dispatch = dispatch; }
        public override IMessage Invoke(IMessage message)
        {
            IMethodCallMessage call = (IMethodCallMessage)message;
            object[] args = call.Args;
            try { return new ReturnMessage(dispatch(call, args), args, args.Length, call.LogicalCallContext, call); }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
    private static T Mock<T>(Func<IMethodCallMessage, object[], object> dispatch) where T : class
    { return (T)new Proxy(typeof(T), dispatch).GetTransparentProxy(); }
    private static object Invoke(string type, string method, params object[] args)
    { return typeof(AppProject).Assembly.GetType("SWBodyOrganizer." + type).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
    private static void Assert(bool value, string label) { if (!value) throw new Exception(label); }
    private static void Reject(Action action, string label)
    { try { action(); } catch (TargetInvocationException ex) { if (ex.InnerException is InvalidDataException) return; throw; } throw new Exception(label); }
    private static int Main()
    {
        try
        {
            double[] matrix = { 1,0,0,0,1,0,0,0,1,.3,.1,0,1,0,0,0 };
            bool coincident = true;
            MathTransform transform = Mock<MathTransform>((call, args) => matrix);
            IBody2 body = Mock<IBody2>((call, args) => {
                switch (call.MethodName)
                {
                    case "get_Name": return "body-A";
                    case "GetMassProperties": return new double[] { 0,0,0,.001,.06 };
                    case "GetFaces": return new object[0];
                    case "GetFaceCount": return 6;
                    case "GetEdgeCount": return 12;
                    case "GetCoincidenceTransform2": args[1] = transform; return coincident;
                    default: throw new Exception("Unexpected body operation: " + call.MethodName);
                }
            });
            ModelDocExtension extension = Mock<ModelDocExtension>((call, args) => {
                Assert(call.MethodName == "GetObjectByPersistReference3", "Unexpected extension operation");
                args[1] = 0; return body;
            });
            bool dirtyRead = false;
            IModelDoc2 model = Mock<IModelDoc2>((call, args) => {
                if (call.MethodName == "get_Extension") return extension;
                if (call.MethodName == "GetSaveFlag") { dirtyRead = true; return true; }
                throw new Exception("Unexpected document operation: " + call.MethodName);
            });
            ISldWorks app = Mock<ISldWorks>((call, args) => {
                Assert(call.MethodName == "IsSame", "Unexpected application operation");
                return (int)swObjectEquality.swObjectSame;
            });
            BodyRecord record = new BodyRecord { SourcePath = @"Z:\nonexistent-source.SLDPRT", SourceSha256 = "",
                OriginalName = "old-name", PersistReference = Convert.ToBase64String(new byte[] { 1,2,3 }) };
            Assert(ReferenceEquals(Invoke("SolidWorksLocator", "ResolveForLocation", app, model, new object[] { body }, record), body) && !dirtyRead,
                "Live persistent identity must work without disk access or dirty-state rejection.");
            Reject(() => Invoke("ExportIntegrity", "VerifyMemory", model, ""), "Export must still reject the same dirty document.");
            record.PersistReference = ""; record.OriginalName = "body-A";
            record.GeometryKey = (string)Invoke("WorkerMain", "BuildGeometryKey", body);
            Assert(ReferenceEquals(Invoke("SolidWorksLocator", "ResolveForLocation", app, model, new object[] { body }, record), body), "V1.2.5 name + fingerprint fallback failed.");
            Reject(() => Invoke("SolidWorksLocator", "ResolveForLocation", app, model, new object[] { body, body }, record), "Ambiguous names must be rejected.");
            record.OriginalName = "missing";
            Reject(() => Invoke("SolidWorksLocator", "ResolveForLocation", app, model, new object[] { body }, record), "A body index must not substitute for identity.");
            MathTransform check;
            Assert(body.GetCoincidenceTransform2(body, out check) && check != null, "Test double must supply the out transform.");
            Assert((bool)Invoke("ExportIntegrity", "ProperRigidTransform", check.ArrayData), "Test double transform must be rigid.");
            Invoke("ExportIntegrity", "VerifySameShape", body, body);
            coincident = false;
            Reject(() => Invoke("ExportIntegrity", "VerifySameShape", body, body), "Equal mass/fingerprint with failed kernel congruence must be rejected.");
            coincident = true; matrix[0] = -1;
            Reject(() => Invoke("ExportIntegrity", "VerifySameShape", body, body), "A reflected match must be rejected.");
            matrix[0] = 1; matrix[12] = 2;
            Reject(() => Invoke("ExportIntegrity", "VerifySameShape", body, body), "A scaled match must be rejected.");
            Console.WriteLine("PASS: live-reference contract (no disk/dirty checks), strict export, legacy fallback, ambiguity, kernel-failure/mirror/scale rejection. Interface doubles only; not a real CAD test.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
