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
using System.Drawing;
using QRCoder;

public class Block
{
    public string Version { get; set; }
    public string LastHash { get; set; }
    public string Hash { get; set; }
    public string Nonce { get; set; }
    public string Time { get; set; }
    public string[] Transactions { get; set; }
    public string[] TransactionTimes { get; set; }


    public static BlockchainUpgrader upgrader = new BlockchainUpgrader();

    public void Upgrade(string toVersion)
    {
        upgrader.Upgrade(this, toVersion);
    }
}

public class WalletInfo
{
    public string Address { get; set; }
    public double Balance { get; set; }
    public double PendingBalance { get; set; }
    public int BlockchainLength { get; set; }
    public int PendingLength { get; set; }
    public string MineDifficulty { get; set; }
    public float CostPerMinute { get; set; }
}

public class DCCPayload : QRCoder.PayloadGenerator.Payload
{
    private string _addr;
    public DCCPayload(string addr)
    {
        _addr = addr;
    }

    public override string ToString()
    {
        return $"dcc://{_addr}";
    }
}

public class Http
{
    public string blockVersion { get; set; }

    public string StartHttpWebRequest(string URL, string[] args_vals)
    {
        string html = string.Empty;

        string url = URL;
        for (int i = 0; i < args_vals.Length; i++)
        {
            if (i > 0)
                url += "&";
            url += args_vals[i];
        }
        url += "&Version=" + blockVersion;

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AutomaticDecompression = DecompressionMethods.GZip;

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream))
        {
            html = reader.ReadToEnd();
        }

        return html;
    }
}


public class clnt
{
    public string username;
    public string password;
    public http httpServ;
    internal static readonly char[] chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

    public static WalletInfo walletInfo = new WalletInfo();

    public Bitmap qrCodeAsBitmap;
    public static int connectionStatus = 1;

    public static string[] directoryList = new string[] { "./wwwdata/blockchain", "./wwwdata/pendingblocks", "./wwwdata/programs" };

    public static string blockVersion = "v0.01alpha-coin";


    public void Client()
    {
        if (walletInfo.Address == null || walletInfo.Address == "")
        {
            foreach (var dir in directoryList)
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

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
            configFileRead = null;
            GC.Collect();

            walletInfo.Address = "dcc" + sha256(username + password);
            walletInfo = GetInfo();
            if (walletInfo == null)
            {
                walletInfo = new WalletInfo();
                walletInfo.Address = "dcc" + sha256(username + password);
                ConnectionError();
            }
            walletInfo.Balance = GetBalance(walletInfo.Address);

            DCCPayload generator = new DCCPayload(walletInfo.Address);
            string payload = generator.ToString();
            QRCodeGenerator qRCodeGenerator = new QRCodeGenerator();
            QRCodeData qRCodeData = qRCodeGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            QRCode qRCode = new QRCode(qRCodeData);
            qrCodeAsBitmap = qRCode.GetGraphic(20);
            if (connectionStatus == 0)
                return;
        }

        walletInfo.Address = "dcc" + sha256(username + password);
        walletInfo = GetInfo();

        if (walletInfo == null)
        {
            walletInfo = new WalletInfo();
            walletInfo.Address = "dcc" + sha256(username + password);
            walletInfo.Balance = GetBalance(walletInfo.Address);
            ConnectionError();
            return;
        }
        else
            connectionStatus = 1;

        if (connectionStatus == 0)
            return;

        walletInfo.Balance = GetBalance(walletInfo.Address);

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
            for (int i = 0; i < walletInfo.BlockchainLength; i++)
            {
                if (SyncBlock(1 + i) == 0)
                {
                    ConnectionError();
                    return;
                }
            }
        }
        connectionStatus = 1;
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
                    Console.WriteLine("Generating Wallet, " + Math.Truncate(((float)i / ((float)sizeInMb * (float)blocksPerMb)) / 2f * (float)100) + "% , " + Math.Truncate((float)i / ((float)sizeInMb * (float)blocksPerMb) * (float)sizeInMb * 100) / 100 + "MB");
                    crypto.GetBytes(data);
                    stream.Write(data, 0, data.Length);
                }
                stream.Write(Encoding.ASCII.GetBytes("==SPLIT=="), 0, Encoding.ASCII.GetBytes("==SPLIT==").Length);
                data = new byte[blockSize];
                for (int a = 0; a < sizeInMb * blocksPerMb; a++)
                {
                    Console.WriteLine("Generating Wallet, " + Math.Truncate((((float)a + (float)sizeInMb * (float)blocksPerMb) / ((float)sizeInMb * (float)blocksPerMb)) / 2f * (float)100) + "% , " + Math.Truncate(((float)a + (float)sizeInMb * (float)blocksPerMb) / ((float)sizeInMb * (float)blocksPerMb) * (float)sizeInMb * 100) / 100 + "MB");
                    crypto.GetBytes(data);
                    stream.Write(data, 0, data.Length);
                }
            }
        }

    }

    WalletInfo GetInfo()
    {
        try
        {
            Http http = new Http();
            http.blockVersion = blockVersion;
            string[] args = new string[] { "query=getInfo", "fromAddress=" + walletInfo.Address, "username=" + username, "password=" + password };
            string html = http.StartHttpWebRequest("http://api.achillium.us.to/dcc/?", args);

            string content = html.Trim();
            return JsonConvert.DeserializeObject<WalletInfo>(content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static int SyncBlock(int whichBlock)
    {
        try
        {
            Http http = new Http();
            http.blockVersion = blockVersion;
            string[] args = new string[] { "query=getBlock", "blockNum=" + whichBlock };
            string html = http.StartHttpWebRequest("http://api.achillium.us.to/dcc/?", args);

            Console.WriteLine("Synced: " + whichBlock);
            File.WriteAllText("./wwwdata/blockchain/block" + whichBlock.ToString() + ".dccblock", html);
            return 1;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    static bool IsChainValid()
    {
        while (Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly).Length < walletInfo.BlockchainLength)
        {
            if (SyncBlock(Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly).Length + 1) == 0)
            {
                ConnectionError();
                break;
            }
        }

        string[] blocks = Directory.GetFiles("./wwwdata/blockchain/", "*.dccblock");

        for (int i = 1; i < blocks.Length; i++)
        {
            string content = File.ReadAllText("./wwwdata/blockchain/block" + i + ".dccblock");
            Block o = JsonConvert.DeserializeObject<Block>(content);
            string[] trans = o.Transactions;

            if (o.Version == null || o.Version == "" || o.Version != blockVersion)
            {
                o.Upgrade(blockVersion);
                File.WriteAllText("./wwwdata/blockchain/block" + i + ".dccblock", JsonConvert.SerializeObject(o));
            }

            string lastHash = o.LastHash;
            string currentHash = o.Hash;
            string nonce = o.Nonce;
            string transactions = JoinArrayPieces(trans);

            content = File.ReadAllText("./wwwdata/blockchain/block" + (i + 1) + ".dccblock");
            o = JsonConvert.DeserializeObject<Block>(content);
            string nextHash = o.LastHash;

            if (o.Version == null || o.Version == "" || o.Version != blockVersion)
            {
                o.Upgrade(blockVersion);
                File.WriteAllText("./wwwdata/blockchain/block" + (i + 1) + ".dccblock", JsonConvert.SerializeObject(o));
            }

            Console.WriteLine("Validating block " + i);
            string blockHash = sha256(lastHash + transactions + nonce);
            if (!blockHash.StartsWith("00") || blockHash != currentHash || blockHash != nextHash)
            {
                return false;
            }
        }
        return true;
    }

    public string Trade(String recipient, float sendAmount)
    {
        Http http = new Http();
        http.blockVersion = blockVersion;
        string[] args = new string[] { "query=sendToAddress", "sendAmount=" + sendAmount, "username=" + username, "password=" + password, "fromAddress=" + walletInfo.Address, "recipientAddress=" + recipient };
        string html = http.StartHttpWebRequest("http://api.achillium.us.to/dcc/?", args);

        walletInfo.Balance = GetBalance(walletInfo.Address);
        Console.WriteLine(html);
        return html.Trim();
    }

    public double GetBalance(string walletAddress)
    {
        double bal = 0.0f;
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
                    if (trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] == walletInfo.Address && trans[l].Trim().Replace("->", ">").Split('>')[2].Split('&')[0] != walletInfo.Address)
                    {
                        bal -= double.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                    }
                    else if (trans[l].Trim().Replace("->", ">").Split('>')[2].Split('&')[0] == walletInfo.Address && trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] != walletInfo.Address)
                    {
                        bal += double.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                    }
                }
                else if (trans[l].Trim().Replace("->", ">").Split('>')[1].Split('&')[0] == walletInfo.Address && trans[l].Trim().Replace("->", ">").Split('>').Length < 3)
                {
                    bal += double.Parse(trans[l].Trim().Replace("->", ">").Split('>')[0]);
                }
            }
        }
        Console.WriteLine("balance:" + (double)bal);
        return bal;
    }

    public int InitializeNewAddress()
    {
        try
        {
            Http http = new Http();
            http.blockVersion = blockVersion;
            string[] args = new string[] { "query=initializeNewAddress", "username=" + username, "password=" + password };
            string html = http.StartHttpWebRequest("http://api.achillium.us.to/dcc/?", args);

            Console.WriteLine(html);
            return 1;
        }
        catch (Exception)
        {
            return 0;
        }
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

    static void ConnectionError()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Failed To Connect");
        Console.ResetColor();
        connectionStatus = 0;
    }
}
