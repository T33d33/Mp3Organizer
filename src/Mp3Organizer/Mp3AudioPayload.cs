using System.Security.Cryptography;

namespace Mp3Organizer;

// Hash the exact bytes between recognized leading/trailing tags. No decoding or encoding.
public static class Mp3AudioPayload
{
    public static string Hash(string path)
    {
        using var stream=new ReadOnlySource().OpenRead(path);var start=0L;var end=stream.Length;
        byte[] Read(long at,int count){stream.Position=at;var bytes=new byte[count];stream.ReadExactly(bytes);return bytes;}
        if(end>=10)
        {
            var h=Read(0,10);
            if(h[0]=='I'&&h[1]=='D'&&h[2]=='3')
            {
                if(h[3] is <2 or >4||h.Skip(6).Any(x=>x>=128))throw new IOException("Unsupported ID3 header; tag write refused.");
                var size=(h[6]<<21)|(h[7]<<14)|(h[8]<<7)|h[9];start=10L+size+(h[3]==4&&(h[5]&16)!=0?10:0);
            }
        }
        if(end>=128){var h=Read(end-128,3);if(h[0]=='T'&&h[1]=='A'&&h[2]=='G')end-=128;}
        if(end>=32)
        {
            var footer=Read(end-32,32);
            if(System.Text.Encoding.ASCII.GetString(footer,0,8)=="APETAGEX")
            {
                var size=BitConverter.ToUInt32(footer,12);if(size<32||size>end-start)throw new IOException("Invalid APE tag size.");
                end-=size;if(end-start>=32&&System.Text.Encoding.ASCII.GetString(Read(end-32,8))=="APETAGEX")end-=32;
            }
        }
        if(start>=end||end-start<4)throw new IOException("No MP3 audio payload found.");
        var header=Read(start,4);if(header[0]!=255||(header[1]&0xe0)!=0xe0||(header[1]&6)!=2||(header[1]&24)==8||(header[2]&0xf0) is 0 or 240||(header[2]&12)==12)
            throw new IOException("Unsupported MP3 payload layout; refusing a tag write without verifiable audio.");
        stream.Position=start;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];
        for(var left=end-start;left>0;){var count=stream.Read(buffer,0,(int)Math.Min(buffer.Length,left));if(count==0)throw new EndOfStreamException();hash.AppendData(buffer,0,count);left-=count;}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
