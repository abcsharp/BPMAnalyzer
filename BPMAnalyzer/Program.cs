using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace BPMAnalyzer
{
	struct RiffHeader
	{
		public string ID;
		public uint Size;
		public string Format;

		public static RiffHeader GetRiffHeader(Stream stream)
		{
			var header=new RiffHeader();
			var rawData=new byte[12];
			stream.Read(rawData,0,rawData.Length);
			header.ID=Encoding.ASCII.GetString(rawData.Take(4).ToArray());
			header.Size=BitConverter.ToUInt32(rawData.Skip(4).Take(4).ToArray(),0);
			header.Format=Encoding.ASCII.GetString(rawData.Skip(8).ToArray());
			return header;
		}
	}

	struct FormatChunk
	{
		public string ID;
		public int Size;
		public short FormatTag;
		public ushort Channels;
		public uint SamplesPerSecond;
		public uint AverageBytesPerSecond;
		public ushort BlockAlign;
		public ushort BitsPerSecond;

		public static FormatChunk GetFormatChunk(Stream stream)
		{
			var formatChunk=new FormatChunk();
			var rawData=new byte[24];
			stream.Read(rawData,0,rawData.Length);
			formatChunk.ID=Encoding.ASCII.GetString(rawData.Take(4).ToArray());
			formatChunk.Size=BitConverter.ToInt32(rawData.Skip(4).Take(4).ToArray(),0);
			formatChunk.FormatTag=BitConverter.ToInt16(rawData.Skip(8).Take(2).ToArray(),0);
			formatChunk.Channels=BitConverter.ToUInt16(rawData.Skip(10).Take(2).ToArray(),0);
			formatChunk.SamplesPerSecond=BitConverter.ToUInt32(rawData.Skip(12).Take(4).ToArray(),0);
			formatChunk.AverageBytesPerSecond=BitConverter.ToUInt32(rawData.Skip(16).Take(4).ToArray(),0);
			formatChunk.BlockAlign=BitConverter.ToUInt16(rawData.Skip(20).Take(2).ToArray(),0);
			formatChunk.BitsPerSecond=BitConverter.ToUInt16(rawData.Skip(22).ToArray(),0);
			return formatChunk;
		}
	}

	struct DataChunk
	{
		public string ID;
		public int Size;

		public static DataChunk GetDataChunk(Stream stream)
		{
			var dataChunk=new DataChunk();
			var rawData=new byte[8];
			stream.Read(rawData,0,rawData.Length);
			dataChunk.ID=Encoding.ASCII.GetString(rawData.Take(4).ToArray());
			dataChunk.Size=BitConverter.ToInt32(rawData.Skip(4).ToArray(),0);
			return dataChunk;
		}
	}

	class WaveFile:IDisposable
	{
		public RiffHeader RiffHeader{get;private set;}
		public FormatChunk FormatChunk{get;private set;}
		public DataChunk DataChunk{get;private set;}
		public short[] Data{get;private set;}

		public WaveFile(string fileName)
		{
			var stream=File.OpenRead(fileName);
			RiffHeader=RiffHeader.GetRiffHeader(stream);
			var pos=stream.Position;
			FormatChunk=FormatChunk.GetFormatChunk(stream);
			stream.Position=pos+FormatChunk.Size+8;
			DataChunk=DataChunk.GetDataChunk(stream);
			if(stream.Length<12+8+FormatChunk.Size+8+DataChunk.Size) throw new InvalidDataException("WAVEファイルの形式が不正です。");
			var waveDataStream=new BinaryReader(stream);
			Data=new short[DataChunk.Size/2];
			unsafe{
				fixed(short* ptr=&Data[0])
					using(var unmanagedStream=new UnmanagedMemoryStream((byte*)ptr,DataChunk.Size,DataChunk.Size,FileAccess.Write))
						stream.CopyTo(unmanagedStream);
			}
			stream.Dispose();
		}

		public void Dispose()
		{
			Data=null;
		}
	}

	class Program
	{
		static double HannWindow(int i,int size)
		{
			return 0.5-0.5*Math.Cos(2.0*Math.PI*i/size);
		}

		static int[] FindPeak(double[] graph,int count)
		{
			var obj=from i in Enumerable.Range(0,graph.Length-1)
					select new{Diff=graph[i+1]-graph[i],Prev=i==0?0:graph[i]-graph[i-1],GraphValue=graph[i],Index=i};
			var indices=from o in obj
						where o.Diff<=0&&o.Prev>0
						orderby o.GraphValue descending
						select o.Index;
			return indices.Take(count).ToArray();
		}

		static void Main(string[] args)
		{
			if(args.Length<1){
				Console.WriteLine("usage: BPMAnalyzer <filename>.wav");
				return;
			}
			var waveFile=new WaveFile(args[0]);
			
			const int frameSize=1024;
			var frameCount=(double)waveFile.FormatChunk.SamplesPerSecond/frameSize;
			var dataLength=waveFile.Data.Length;
			var sampleCount=dataLength/frameSize/2;
			var data=waveFile.Data.ToList();
			var volume=(from index in Enumerable.Range(0,sampleCount)
						let sum=data.GetRange(frameSize*index,frameSize).Sum(d=>(double)d*d)
						select Math.Sqrt(sum/frameSize)).ToArray();

			waveFile.Dispose();

			var prev=0.0;
			var diff=(from v in volume let temp=prev select Math.Max((prev=v)-temp,0.0)).ToArray();
			
			var a=new double[240-60+1];
			var b=new double[a.Length];
			var r=new double[a.Length];
			for(int i=0;i<a.Length;i++){
				double sum1=0,sum2=0,freq=(i+60)/60.0;
				var theta=2.0*Math.PI*freq/frameCount;
				for(int n=0;n<diff.Length;n++){
					var window=HannWindow(n,diff.Length);
					sum1+=diff[n]*Math.Cos(theta*n)*window;
					sum2+=diff[n]*Math.Sin(theta*n)*window;
				}
				a[i]=sum1/sampleCount;
				b[i]=sum2/sampleCount;
				r[i]=Math.Sqrt(a[i]*a[i]+b[i]*b[i]);
			}

			var peaks=FindPeak(r,3);
			for(int index=0;index<peaks.Length;index++){
				Console.WriteLine("[{0}]",index+1);
				int bpm=peaks[index]+60;
				Console.WriteLine("peak bmp: {0}",bpm);
				var theta=Math.Atan2(b[peaks[index]],a[peaks[index]]);
				if(theta<0) theta+=2.0*Math.PI;
				var peakFreq=(double)bpm/60;
				var startTime=theta/(2.0*Math.PI*peakFreq);
				var startBeat=theta/(2.0*Math.PI);
				Console.WriteLine("first beat time: {0} sec",startTime);
				Console.WriteLine("first beat: {0} beat",startBeat);
			}
		}
	}
}
