using System;using MasterMiao.Weld;
class ClusterCheck{
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Main(){int cases=0;foreach(int count in new[]{1,2,3,4,8,16,50})foreach(double size in new[]{.0001,.001,.01,.1,1})foreach(double aspect in new[]{.1,1,10}){
  double area=size*size;var g=BadgeCluster.Plan(count,size*size*size,area,size*aspect,size/aspect,1.67);
  Check(g.Height<=.010+1e-12&&g.Width<=.010+1e-12,"10mm limit");Check(g.ClusterWidth*g.ClusterHeight<=area*.25+1e-12,"cluster area limit");Check(g.ClusterWidth<=size*aspect&&g.ClusterHeight<=size/aspect,"face bounds");
  for(int i=0;i<count;i++){var a=g.Offset(i);Check(Math.Abs(a[0])+g.Width/2<=g.ClusterWidth/2+1e-12&&Math.Abs(a[1])+g.Height/2<=g.ClusterHeight/2+1e-12,"badge within cluster");for(int j=0;j<i;j++){var b=g.Offset(j);Check(Math.Abs(a[0]-b[0])>=g.Width-1e-12||Math.Abs(a[1]-b[1])>=g.Height-1e-12,"nonoverlapping badges");}}
  double h=g.Height;g.Shrink(.8);Check(Math.Abs(g.Height/h-.8)<1e-12,"uniform shrink");cases++;
 }
 var small=BadgeCluster.Plan(2,1e-9,1,1,1,1);var large=BadgeCluster.Plan(2,8e-9,1,1,1,1);Check(Math.Abs(large.Height/small.Height-2)<1e-12,"volume scaling");Console.WriteLine("PASS "+cases+" layout cases, volume scaling, area, size, overlap, bounds and uniform shrink");}
}
