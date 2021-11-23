/*       Client Program      */

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;

class Block
{
    public string LastHash { get; set; }
    public string Hash { get; set; }
    public string Nonce { get; set; }
    public string Time { get; set; }
    public string[] Transactions { get; set; }
    public string[] TransactionTimes { get; set; }
}
class WalletInfo
{
    public string Address { get; set; }
    public float Balance { get; set; }
    public float PendingBalance { get; set; }
    public int BlockchainLength { get; set; }
    public int PendingLength { get; set; }
    public string MineDifficulty { get; set; }
    public float CostPerMinute { get; set; }
}
public class clnt
{
    public int blockchainlength = 0;
    public float balance;
    public float pendingBalance;
    public string username;
    public string password;
    public string wallet;
    public float costPerMinute;
    public http httpServ;
    internal static readonly char[] chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();
    static int pendingLength = 0;
    static int blockChainLength = 0;
    static int totalBlocks = 0;
    static string lengths = null;

    public void Client()
    {
        if(wallet == null || wallet == "")
        {
            Process proc = new Process();
            proc.StartInfo.FileName = "netsh";
            proc.StartInfo.Arguments = "http add urlacl url = http://74.78.145.2:8000/ user=Everyone";
            Console.WriteLine(proc.StartInfo.Arguments);
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();

            string configFileRead = File.ReadAllText("./cold-wallet.dccwallet");
            if (configFileRead.Length > 4)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Checking keys...");
                Console.ResetColor();
                username = sha256(configFileRead.Split(new string[] { "==SPLIT==" }, StringSplitOptions.RemoveEmptyEntries)[0].Trim());
                password = sha256(configFileRead.Split(new string[] { "==SPLIT==" }, StringSplitOptions.RemoveEmptyEntries)[1].Trim());
            }
            else
            {
                CreateRandomFile("./cold-wallet.dccwallet", 64);
                configFileRead = File.ReadAllText("./cold-wallet.dccwallet");

                username = sha256(configFileRead.Split(new string[] { "==SPLIT==" }, StringSplitOptions.RemoveEmptyEntries)[0].Trim());
                password = sha256(configFileRead.Split(new string[] { "==SPLIT==" }, StringSplitOptions.RemoveEmptyEntries)[1].Trim());

                Console.WriteLine(username);
                Console.WriteLine(password);

                InitializeNewAddress();
            }
            GC.Collect();

            wallet = "dcc" + sha256(username + password);
        }

        lengths = GetLength();
        try
        {
            pendingLength = int.Parse(lengths.Split('#')[0]);
            blockChainLength = int.Parse(lengths.Split('#')[1]);
        }
        catch (Exception)
        {
            pendingLength = 0;
            blockChainLength = 0;
        }

        while (Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly).Length < blockChainLength)
        {
            SyncBlock(Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly).Length + 1);
        }

        if (!IsChainValid())
        {
            foreach (string oldBlock in Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(oldBlock);
                }
                catch (Exception)
                {
                }
            }
            for (int i = 0; i < blockChainLength; i++)
            {
                SyncBlock(1 + i);
            }
        }

        balance = GetBalance(wallet);
        pendingBalance = GetPendingBalance(wallet);
        costPerMinute = GetCostPerMinute();
    }

    private void CreateRandomFile(string filePath, int sizeInMb)
    {
        // Note: block size must be a factor of 1MB to avoid rounding errors
        const int blockSize = 1024 * 8;
        const int blocksPerMb = (1024 * 1024) / blockSize;

        byte[] data = new byte[blockSize];

        using (RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider())
        {
            using (FileStream stream = File.OpenWrite(filePath))
            {
                for (int i = 0; i < sizeInMb * blocksPerMb; i++)
                {
                    Console.WriteLine("Generating Wallet, " + Math.Truncate(((float)i / ((float)sizeInMb * (float)blocksPerMb)) / 2f* (float)100) + "% , " + Math.Truncate((float)i / ((float)sizeInMb * (float)blocksPerMb) * (float)sizeInMb*100)/100 + "MB");
                    crypto.GetBytes(data);
                    stream.Write(data, 0, data.Length);
                }
                stream.Write(Encoding.ASCII.GetBytes("==SPLIT=="), 0, Encoding.ASCII.GetBytes("==SPLIT==").Length);
                data = new byte[blockSize];
                for (int a = 0; a < sizeInMb * blocksPerMb; a++)
                {
                    Console.WriteLine("Generating Wallet, " + Math.Truncate((((float)a + (float)sizeInMb * (float)blocksPerMb) / ((float)sizeInMb * (float)blocksPerMb)) / 2f* (float)100) + "% , " + Math.Truncate(((float)a + (float)sizeInMb * (float)blocksPerMb) / ((float)sizeInMb * (float)blocksPerMb) * (float)sizeInMb * 100)/100 + "MB");
                    crypto.GetBytes(data);
                    stream.Write(data, 0, data.Length);
                }
            }
        }

    }

    public static string GetUniqueKey(int size)
    {
        byte[] data = new byte[4 * size];
        using (var crypto = RandomNumberGenerator.Create())
        {
            crypto.GetBytes(data);
        }
        StringBuilder result = new StringBuilder(size);
        for (int i = 0; i < size; i++)
        {
            var rnd = BitConverter.ToUInt32(data, i * 4);
            var idx = rnd % chars.Length;

            result.Append(chars[idx]);
        }

        return result.ToString();
    }

    static string GetLength()
    {
        try
        {
            string lengths = "";

            string html = string.Empty;
            string url = @"http://api.achillium.us.to/dcc/?query=amountOfPendingBlocks";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                html = reader.ReadToEnd();
            }
            lengths += html.Trim();


            html = string.Empty;
            url = @"http://api.achillium.us.to/dcc/?query=amountOfCompletedBlocks";

            request = (HttpWebRequest)WebRequest.Create(url);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                html = reader.ReadToEnd();
            }
            lengths += "#" + html.Trim();

            return lengths;
        }
        catch (Exception e)
        {
            Console.WriteLine("Error, Try again later" + e.StackTrace);
            return "";
        }
    }

    static void SyncBlock(int whichBlock)
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=getBlock&blockNum=" + whichBlock;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }

        Console.WriteLine("Synced: " + whichBlock);
        File.WriteAllText("./wwwdata/blockchain/block" + whichBlock.ToString() + ".txt", html);
    }

    static bool IsChainValid()
    {
        string[] blocks = Directory.GetFiles("./wwwdata/blockchain/", "*.txt");

        for (int i = 1; i < blocks.Length; i++)
        {
            string content = File.ReadAllText("./wwwdata/blockchain/block" + i + ".txt");
            Block o = JsonConvert.DeserializeObject<Block>(content);
            string[] trans = o.Transactions;

            string lastHash = o.LastHash;
            string currentHash = o.Hash;
            string nonce = o.Nonce;
            string transactions = JoinArrayPieces(trans);

            string nextHash = "";

            content = File.ReadAllText("./wwwdata/blockchain/block" + (i + 1) + ".txt");
            o = JsonConvert.DeserializeObject<Block>(content);
            nextHash = o.Hash;

            Console.WriteLine("Validating block " + i);
            string blockHash = sha256(lastHash + transactions + nonce);
            if (!blockHash.StartsWith("00") || blockHash != currentHash || blockHash != nextHash)
            {
                return false;
            }
        }
        return true;
    }

    void GetInfo()
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=getWalletInfo&fromAddress=" + wallet + "&username=" + username + "&password=" + password;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }
        
        string content = html.Trim();
        walletInfo = JsonConvert.DeserializeObject<WalletInfo>(content);
    }
    
    public string Trade(String recipient, float sendAmount)
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=sendToAddress&sendAmount=" + sendAmount + "&username=" + username + "&password=" + password + "&fromAddress=" + wallet + "&recipientAddress=" + recipient;
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }
        balance = GetBalance(wallet);
        Console.WriteLine(html);
        return html.Trim();
    }

    public string UploadProgram(string fileName, float minutes, int computationLevel)
    {
        string baseName = fileName.Split('\\')[fileName.Split('\\').Length - 1];

        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=uploadProgram&fileName=" + baseName + "&username=" + username + "&password=" + password + "&fromAddress=" + wallet + "&minutes=" + minutes + "&computationLevel=" + computationLevel;

        System.Net.WebClient Client = new System.Net.WebClient();
        Client.Headers.Add("Content-Type", "binary/octet-stream");
        byte[] result = Client.UploadFile(url, "POST", fileName);
        string s = System.Text.Encoding.UTF8.GetString(result, 0, result.Length);

        Console.WriteLine(s);
        return s.Trim(); ;
    }

    public void Help()
    {
        Console.WriteLine("Possible Actions:\n");
        Console.WriteLine("trade: send tokens to anybody");
    }

    public float GetBalance(string walletAddress)
    {
        float bal = 0f;
        string[] blocks = Directory.GetFiles("./wwwdata/blockchain/");

        for (int i = 0; i < blocks.Length; i++)
        {
            string content = File.ReadAllText(blocks[i]);

            Block o = JsonConvert.DeserializeObject<Block>(content);
            string[] trans = o.Transactions;

            for (int l = 0; l < trans.Length; l++)
            {
                if (trans[l].Trim().Replace("->", ">").Split('>').Length >= 3)
                {
                    if (trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] == wallet && trans[l].Trim().Replace("->", ">").Split('>')[2].Split('&')[0] != wallet)
                    {
                        bal -= float.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                    }
                    else if (trans[l].Trim().Replace("->", ">").Split('>')[2].Split('&')[0] == wallet && trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] != wallet)
                    {
                        bal += float.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                    }
                }
                else if (trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] == wallet && trans[l].Trim().Replace("->", ">").Split('>').Length < 3)
                {
                    bal += float.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                }
            }
        }

        return (float)Math.Truncate(bal * 10000) / 10000;

        //string html = string.Empty;
        //string url = @"http://api.achillium.us.to/dcc/?query=getBalance&fromAddress=" + wallet + "&username=" + username + "&password=" + password;

        //HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        //request.AutomaticDecompression = DecompressionMethods.GZip;

        //using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        //using (Stream stream = response.GetResponseStream())
        //using (StreamReader reader = new StreamReader(stream))
        //{
        //	html = reader.ReadToEnd();
        //}

        //Console.WriteLine(html);
        //return float.Parse(html.Trim());
    }

    public float GetPendingBalance(string walletAddress)
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=pendingFunds&fromAddress=" + wallet + "&username=" + username + "&password=" + password;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }

        Console.WriteLine(html);

        return (float)Math.Truncate(float.Parse(html.Trim()) * 10000) / 10000;
    }

    public float GetCostPerMinute()
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=getCostPerMinute";

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }

        Console.WriteLine(html);
        return float.Parse(html.Trim());
    }

    public void InitializeNewAddress()
    {
        string html = string.Empty;
        string url = @"http://api.achillium.us.to/dcc/?query=initializeNewAddress" + "&username=" + username + "&password=" + password;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }

        Console.WriteLine(html);
    }

    static string sha256(string input)
    {
        var crypt = new System.Security.Cryptography.SHA256Managed();
        var hash = new System.Text.StringBuilder();
        byte[] crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(input));
        foreach (byte theByte in crypto)
        {
            hash.Append(theByte.ToString("x2"));
        }
        return hash.ToString();
    }

    static string JoinArrayPieces(string[] input)
    {
        string outStr = "";
        foreach (string str in input)
        {
            outStr += str;
        }
        return outStr;
    }
}
