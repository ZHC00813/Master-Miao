using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Connects only to the explicitly named existing SW process. Temporary Modeler bodies
// never become documents and are never saved. Run only between worker tasks.
internal static class VerifySolidGeometry
{
    private static readonly List<object> owned = new List<object>();
    [STAThread]
    private static int Main(string[] args)
    {
        ISldWorks sw = null;
        try
        {
            if (args.Length != 2) throw new ArgumentException("Usage: VerifySolidGeometry <MasterMiao.exe> <expected SolidWorks PID>");
            sw = Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
            if (sw == null || sw.GetProcessID() != int.Parse(args[1])) throw new InvalidOperationException("SolidWorks session identity differs; no test run.");
            IModeler modeler = Keep(sw.GetModeler() as IModeler);
            IMathUtility math = Keep(sw.GetMathUtility() as IMathUtility);
            Assembly app = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));
            MethodInfo verify = app.GetType("SWBodyOrganizer.ExportIntegrity",true).GetMethod("VerifyGeometry",BindingFlags.Static|BindingFlags.NonPublic);
            MethodInfo duplicate = app.GetType("SWBodyOrganizer.ExportIntegrity",true).GetMethod("VerifySameShape",BindingFlags.Static|BindingFlags.NonPublic);
            MethodInfo key = app.GetType("SWBodyOrganizer.WorkerMain",true).GetMethod("BuildGeometryKey",BindingFlags.Static|BindingFlags.NonPublic);

            IBody2 plate = Plate(modeler, .025);
            IBody2 same = Keep(plate.Copy() as IBody2);
            verify.Invoke(null,new object[]{plate,same});
            duplicate.Invoke(null,new object[]{plate,same});
            Console.WriteLine("PASS: actual identical solid passes in-place verification.");
            IBody2 otherSpacing = Plate(modeler, .018);
            EqualMass(plate,otherSpacing);
            if (!Equals(key.Invoke(null,new object[]{plate}),key.Invoke(null,new object[]{otherSpacing}))) throw new Exception("Fixture did not reproduce equal candidate fingerprints.");
            Reject(verify,plate,otherSpacing,"same volume/area/fingerprint but different hole spacing");
            Reject(duplicate,plate,otherSpacing,"duplicate with different hole spacing");

            double[] translation = {1,0,0,0,1,0,0,0,1,.13,-.04,.07,1,0,0,0};
            IBody2 moved = Transform(plate,math,translation);
            RigidCongruence(plate,moved,"translated identical solid");
            duplicate.Invoke(null,new object[]{plate,moved});
            Reject(verify,plate,moved,"translated solid is not original placement");
            double angle=.43,c=Math.Cos(angle),s=Math.Sin(angle);
            double[] rotation={c,s,0,-s,c,0,0,0,1,0,0,0,1,0,0,0};
            IBody2 rotated=Transform(plate,math,rotation);
            RigidCongruence(plate,rotated,"rotated identical solid");
            duplicate.Invoke(null,new object[]{plate,rotated});
            Reject(verify,plate,rotated,"rotated solid is not original placement");

            IBody2 right=PocketPlate(modeler,false), left=PocketPlate(modeler,true);
            EqualMass(right,left);
            Reject(verify,right,left,"asymmetric mirrored blind-pocket plate");
            Reject(duplicate,right,left,"duplicate with asymmetric mirrored pockets");
            MathTransform mirrorMatch=null;
            bool canMatch=right.GetCoincidenceTransform2(left,out mirrorMatch);
            if(mirrorMatch!=null) Keep(mirrorMatch);
            if(canMatch && PositiveRigid(mirrorMatch.ArrayData as double[])) throw new Exception("Mirrored fixture unexpectedly admits a proper rigid congruence.");
            Console.WriteLine("PASS: actual asymmetric mirror admits no proper rigid congruence.");
            Console.WriteLine("PASS: real SolidWorks Modeler verifies duplicate congruence and strict in-place export geometry. This does not exercise the full export workflow.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally
        {
            for(int i=owned.Count-1;i>=0;i--) if(owned[i]!=null&&Marshal.IsComObject(owned[i])) try{Marshal.ReleaseComObject(owned[i]);}catch{}
            if(sw!=null) try{Marshal.ReleaseComObject(sw);}catch{}
        }
    }
    private static T Keep<T>(T value) where T:class { if(value==null) throw new Exception("SolidWorks fixture object creation failed."); owned.Add(value); return value; }
    private static IBody2 Box(IModeler modeler,double x,double y,double z,double width,double length,double height)
    {
        return Keep(modeler.CreateBodyFromBox3(new[]{x,y,z,0.0,0.0,1.0,width,length,height}) as IBody2);
    }
    private static IBody2 Cut(IBody2 body,IBody2 tool)
    {
        int error;
        object[] values=body.Operations2((int)swBodyOperationType_e.SWBODYCUT,tool,out error) as object[];
        if(error!=0||values==null||values.Length!=1) throw new Exception("Fixture boolean cut failed, error="+error);
        return Keep(values[0] as IBody2);
    }
    private static IBody2 Plate(IModeler modeler,double spacing)
    {
        IBody2 body=Box(modeler,0,0,0,.1,.08,.01);
        body=Cut(body,Box(modeler,-spacing,0,-.001,.01,.01,.012));
        return Cut(body,Box(modeler,spacing,0,-.001,.01,.01,.012));
    }
    private static IBody2 PocketPlate(IModeler modeler,bool mirror)
    {
        double sign=mirror?-1:1;
        IBody2 body=Box(modeler,0,0,0,.1,.08,.02);
        body=Cut(body,Box(modeler,sign*(-.025),-.015,.01,.012,.01,.011));
        return Cut(body,Box(modeler,sign*.023,.02,.015,.008,.012,.006));
    }
    private static IBody2 Transform(IBody2 body,IMathUtility math,double[] values)
    {
        IBody2 copy=Keep(body.Copy() as IBody2);
        MathTransform transform=Keep(math.CreateTransform(values) as MathTransform);
        if(!copy.ApplyTransform(transform)) throw new Exception("Fixture transform failed.");
        return copy;
    }
    private static void EqualMass(IBody2 a,IBody2 b)
    {
        double[] x=a.GetMassProperties(1) as double[],y=b.GetMassProperties(1) as double[];
        if(x==null||y==null||Math.Abs(x[3]-y[3])>1e-12||Math.Abs(x[4]-y[4])>1e-10) throw new Exception("Fixture mass/area are not equal.");
    }
    private static bool PositiveRigid(double[] a)
    {
        if(a==null||a.Length<13) return false;
        double det=a[0]*(a[4]*a[8]-a[5]*a[7])-a[1]*(a[3]*a[8]-a[5]*a[6])+a[2]*(a[3]*a[7]-a[4]*a[6]);
        return Math.Abs(det-1)<1e-8&&Math.Abs(a[12]-1)<1e-8;
    }
    private static void RigidCongruence(IBody2 a,IBody2 b,string label)
    {
        MathTransform transform;
        if(!a.GetCoincidenceTransform2(b,out transform)||transform==null) throw new Exception("Expected rigid congruence: "+label);
        Keep(transform);
        if(!PositiveRigid(transform.ArrayData as double[])) throw new Exception("Expected proper rigid transform: "+label);
        Console.WriteLine("PASS: kernel recognizes "+label+" (candidate only).");
    }
    private static void Reject(MethodInfo verify,IBody2 a,IBody2 b,string label)
    {
        try { verify.Invoke(null,new object[]{a,b}); }
        catch(TargetInvocationException ex)
        {
            if(ex.InnerException is System.IO.InvalidDataException) { Console.WriteLine("PASS: rejected "+label); return; }
            throw;
        }
        throw new Exception("Failed to reject "+label);
    }
}
