using System.IO;
using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using UdtSharp;
using ExtensionMethods;


public class P2P
{
    public string blockVersion { get; set; }
    static string p2pURL = "http://api.achillium.us.to/dcc/p2pconn.php?";

    public string Connect(string url)
    {
        SessionSettings session = new SessionSettings();

        string remoteIp;
        int remotePort;

        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Http http = new Http();
        string httpStatus;

        try
        {
            if (session.LocalPort != -1)
            {
                ConsoleEx.WriteLine("Using local port: " + session.LocalPort, ConsoleEx.Network());
            }
            else
            {
                session.LocalPort = 0;
            }
            socket.Bind(new IPEndPoint(IPAddress.Any, session.LocalPort));

            P2pEndPoint p2pEndPoint;
            while (true)
            {
                try
                {
                    p2pEndPoint = GetExternalEndPoint(socket);
                    break;
                }
                catch (Exception)
                {
                    ConsoleEx.WriteLine("Failed to connect to Stun server", ConsoleEx.Error());
                    continue;
                }
            }

            if (p2pEndPoint == null)
                return "";

            ConsoleEx.WriteLine("IP at: " + p2pEndPoint.External.ToString(), ConsoleEx.Network());
            ConsoleEx.WriteLine("Connecting to server at: " + p2pURL, ConsoleEx.Network());
            ConsoleEx.WriteLine();

            httpStatus = http.StartHttpWebRequest(p2pURL, new string[] {
                "query=AskForConnection",
                "ip_port=" + p2pEndPoint.External.ToString(),
                "last_tried_ip_port=none"
            });
            ConsoleEx.WriteLine(httpStatus, ConsoleEx.Network());

            ConsoleEx.WriteLine();

            string last_tried_ip_port = "";
            while (true)
            {
                httpStatus = http.StartHttpWebRequest(p2pURL, new string[] {
                    "query=WaitForConnection",
                    "ip_port=" + p2pEndPoint.External.ToString(),
                    "last_tried_ip_port=" + last_tried_ip_port
                });

                if (httpStatus == "" || httpStatus.Contains("waiting") || httpStatus.Contains(p2pEndPoint.External.ToString()))
                {
                    DateTime now = InternetTime.Get();

                    int sleepTimeToSync = SleepTime(now);

                    ConsoleEx.WriteLine("[" + now.ToLongTimeString() + "] - Waiting " + sleepTimeToSync + " sec to look for a new peer", ConsoleEx.Network());
                    System.Threading.Thread.Sleep(sleepTimeToSync * 1000);
                    continue;
                }

                last_tried_ip_port = httpStatus;

                string peer = httpStatus;

                // try again to connect to external to "reopen" port
                //GetExternalEndPoint(socket);

                try
                {
                    ParseRemoteAddr(peer, out remoteIp, out remotePort);
                }
                catch (Exception)
                {
                    continue;
                }

                UdtSocket connection = PeerConnect(socket, remoteIp, remotePort);

                //if (connection == null)
                //{
                //    ConsoleEx.WriteLine("Failed to establish P2P conn to " + remoteIp + ", retrying " + (2 - connectionAttempt) + " more times...", ConsoleEx.Error());
                //    continue;
                //}

                try
                {
                    using (var netStream = new UdtNetworkStream(connection))
                    using (var writer = new BinaryWriter(netStream))
                    using (var reader = new BinaryReader(netStream))
                    {
                        int blockchainLength = Directory.GetFiles("./wwwdata/blockchain/", "*.*", SearchOption.TopDirectoryOnly).Length;
                        writer.Write(blockchainLength);
                        int peerBlockchainLength = reader.ReadInt32();

                        ConsoleEx.WriteLine("Your blockchain length: " + blockchainLength, ConsoleEx.Network());
                        ConsoleEx.WriteLine("Peer blockchain length: " + peerBlockchainLength, ConsoleEx.Network());

                        while (true)
                        {
                            httpStatus = http.StartHttpWebRequest(p2pURL, new string[] {
                                "query=IsStarterOfConnection",
                                "ip_port=" + p2pEndPoint.External.ToString(),
                                "last_tried_ip_port=" + last_tried_ip_port
                            }).Trim();
                            if (httpStatus == "yes" || httpStatus == "no")
                                break;
                        }

                        if (httpStatus == "yes")
                        {
                            //// If the url contains a page address
                            //if (url != "")
                            //{
                            ConsoleEx.WriteLine("sent: " + last_tried_ip_port + url, ConsoleEx.Network());
                            writer.Write(url);
                            string s = reader.ReadString();
                            ConsoleEx.WriteLine("got: " + s, ConsoleEx.Network());
                            return s;
                            //}
                        }
                        else
                        {
                            string str = reader.ReadString();
                            if (str.Contains("php"))
                            {
                                PHPServ serv = new PHPServ();
                                string s = serv.Serve(str);
                                ConsoleEx.WriteLine("php-out: " + s, ConsoleEx.Network());
                                writer.Write(s);
                            }
                        }
                    }


                    //if (session.Sender)
                    //{
                    //    Sender.Run(connection, session.Data, session.Verbose);
                    //    return;
                    //}

                    //Receiver.Run(connection);
                    break;
                }
                finally
                {
                    httpStatus = http.StartHttpWebRequest(p2pURL, new string[] {
                        "query=SucceededConnection",
                        "ip_port=" + p2pEndPoint.External.ToString(),
                        "last_tried_ip_port=none"
                    });
                    connection.Close();
                }
            }
        }
        finally
        {
            socket.Close();
        }

        return "";
    }

    class SessionSettings
    {
        internal bool Sender = false;

        internal bool Receiver = false;

        internal string Data;

        internal int LocalPort = -1;

        internal bool Verbose = false;
    }

    static void ParseRemoteAddr(string addr, out string remoteIp, out int port)
    {
        string[] split = addr.Split(':');

        remoteIp = split[0];
        port = int.Parse(split[1]);
    }

    class P2pEndPoint
    {
        internal IPEndPoint External;
        internal IPEndPoint Internal;
    }

    static int SleepTime(DateTime now)
    {
        List<int> seconds = new List<int>() { 10, 20, 30, 40, 50, 60 };

        int next = seconds.Find(x => x > now.Second);

        return next - now.Second;
    }

    static UdtSocket PeerConnect(Socket socket, string remoteAddr, int remotePort)
    {
        bool bConnected = false;
        int retry = 0;

        UdtSocket client = null;

        while (!bConnected && retry < 11)
        {
            try
            {
                //DateTime now = InternetTime.Get();

                //int sleepTimeToSync = SleepTime(now);

                //ConsoleEx.WriteLine("[" + now.ToLongTimeString() + "] - Waiting " + sleepTimeToSync + " sec to sync with other peer", ConsoleEx.Network());
                //System.Threading.Thread.Sleep(sleepTimeToSync * 1000);

                //GetExternalEndPoint(socket);

                if (client != null)
                    client.Close();

                client = new UdtSocket(socket.AddressFamily, socket.SocketType);
                client.Bind(socket);

                ConsoleEx.WriteLine("\r" + retry + " - Trying to connect to " + remoteAddr + ":" + remotePort + ".  ", ConsoleEx.Network());
                retry++;

                client.Connect(new IPEndPoint(IPAddress.Parse(remoteAddr), remotePort));

                ConsoleEx.WriteLine("Connected successfully to " + remoteAddr + ":" + remotePort);

                bConnected = true;
            }
            catch (Exception e)
            {
                //ConsoleEx.Write(e.Message.Replace(Environment.NewLine, ". "));
            }
        }

        return client;
    }

    static P2pEndPoint GetExternalEndPoint(Socket socket)
    {
        // https://gist.github.com/zziuni/3741933

        List<Tuple<string, int>> stunServers = new List<Tuple<string, int>>();
        stunServers.Add(new Tuple<string, int>("stun.l.google.com", 19302));
        stunServers.Add(new Tuple<string, int>("iphone-stun.strato-iphone.de", 3478));
        stunServers.Add(new Tuple<string, int>("numb.viagenie.ca", 3478));
        stunServers.Add(new Tuple<string, int>("s1.taraba.net", 3478));
        stunServers.Add(new Tuple<string, int>("s2.taraba.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.12connect.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.12voip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.1und1.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.2talk.co.nz", 3478));
        stunServers.Add(new Tuple<string, int>("stun.2talk.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.3clogic.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.3cx.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.a-mm.tv", 3478));
        stunServers.Add(new Tuple<string, int>("stun.aa.net.uk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.acrobits.cz", 3478));
        stunServers.Add(new Tuple<string, int>("stun.actionvoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.advfn.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.aeta-audio.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.aeta.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.alltel.com.au", 3478));
        stunServers.Add(new Tuple<string, int>("stun.altar.com.pl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.annatel.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.antisip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.arbuz.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.avigora.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.avigora.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.awa-shima.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.awt.be", 3478));
        stunServers.Add(new Tuple<string, int>("stun.b2b2c.ca", 3478));
        stunServers.Add(new Tuple<string, int>("stun.bahnhof.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.barracuda.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.bluesip.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.bmwgs.cz", 3478));
        stunServers.Add(new Tuple<string, int>("stun.botonakis.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.budgetphone.nl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.budgetsip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cablenet-as.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.callromania.ro", 3478));
        stunServers.Add(new Tuple<string, int>("stun.callwithus.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cbsys.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.chathelp.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cheapvoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ciktel.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cloopen.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.colouredlines.com.au", 3478));
        stunServers.Add(new Tuple<string, int>("stun.comfi.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.commpeak.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.comtube.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.comtube.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cope.es", 3478));
        stunServers.Add(new Tuple<string, int>("stun.counterpath.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.counterpath.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.cryptonit.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.darioflaccovio.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.datamanagement.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.dcalling.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.decanet.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.demos.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.develz.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.dingaling.ca", 3478));
        stunServers.Add(new Tuple<string, int>("stun.doublerobotics.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.drogon.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.duocom.es", 3478));
        stunServers.Add(new Tuple<string, int>("stun.dus.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.e-fon.ch", 3478));
        stunServers.Add(new Tuple<string, int>("stun.easybell.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.easycall.pl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.easyvoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.efficace-factory.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.einsundeins.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.einsundeins.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ekiga.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.epygi.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.etoilediese.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.eyeball.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.faktortel.com.au", 3478));
        stunServers.Add(new Tuple<string, int>("stun.freecall.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.freeswitch.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.freevoipdeal.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.fuzemeeting.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.gmx.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.gmx.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.gradwell.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.halonet.pl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.hellonanu.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.hoiio.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.hosteurope.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ideasip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.imesh.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.infra.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.internetcalls.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.intervoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ipcomms.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ipfire.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ippi.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ipshka.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.iptel.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.irian.at", 3478));
        stunServers.Add(new Tuple<string, int>("stun.it1.hr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ivao.aero", 3478));
        stunServers.Add(new Tuple<string, int>("stun.jappix.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.jumblo.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.justvoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.kanet.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.kiwilink.co.nz", 3478));
        stunServers.Add(new Tuple<string, int>("stun.kundenserver.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.linea7.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.linphone.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.liveo.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.lowratevoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.lugosoft.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.lundimatin.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.magnet.ie", 3478));
        stunServers.Add(new Tuple<string, int>("stun.manle.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.mgn.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.mit.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.mitake.com.tw", 3478));
        stunServers.Add(new Tuple<string, int>("stun.miwifi.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.modulus.gr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.mozcom.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.myvoiptraffic.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.mywatson.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nas.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.neotel.co.za", 3478));
        stunServers.Add(new Tuple<string, int>("stun.netappel.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.netappel.fr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.netgsm.com.tr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nfon.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.noblogs.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.noc.ams-ix.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.node4.co.uk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nonoh.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nottingham.ac.uk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nova.is", 3478));
        stunServers.Add(new Tuple<string, int>("stun.nventure.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.on.net.mk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ooma.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ooonet.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.oriontelekom.rs", 3478));
        stunServers.Add(new Tuple<string, int>("stun.outland-net.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ozekiphone.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.patlive.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.personal-voip.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.petcube.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.phone.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.phoneserve.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.pjsip.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.poivy.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.powerpbx.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.powervoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ppdi.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.prizee.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.qq.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.qvod.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rackco.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rapidnet.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rb-net.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.refint.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.remote-learner.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rixtelecom.se", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rockenstein.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rolmail.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rounds.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.rynga.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.samsungsmartcam.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.schlund.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.services.mozilla.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sigmavoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sip.us", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sipdiscount.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sipgate.net", 10000));
        stunServers.Add(new Tuple<string, int>("stun.sipgate.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.siplogin.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sipnet.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sipnet.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.siportal.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sippeer.dk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.siptraffic.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.skylink.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sma.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.smartvoip.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.smsdiscount.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.snafu.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.softjoys.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.solcon.nl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.solnet.ch", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sonetel.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sonetel.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sovtest.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.speedy.com.ar", 3478));
        stunServers.Add(new Tuple<string, int>("stun.spokn.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.srce.hr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ssl7.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.stunprotocol.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.symform.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.symplicity.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.sysadminman.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.t-online.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.tagan.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.tatneft.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.teachercreated.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.tel.lu", 3478));
        stunServers.Add(new Tuple<string, int>("stun.telbo.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.telefacil.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.tis-dialog.ru", 3478));
        stunServers.Add(new Tuple<string, int>("stun.tng.de", 3478));
        stunServers.Add(new Tuple<string, int>("stun.twt.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.u-blox.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ucallweconn.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ucsb.edu", 3478));
        stunServers.Add(new Tuple<string, int>("stun.ucw.cz", 3478));
        stunServers.Add(new Tuple<string, int>("stun.uls.co.za", 3478));
        stunServers.Add(new Tuple<string, int>("stun.unseen.is", 3478));
        stunServers.Add(new Tuple<string, int>("stun.usfamily.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.veoh.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vidyo.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vipgroup.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.virtual-call.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.viva.gr", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vivox.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vline.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vo.lu", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vodafone.ro", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voicetrading.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voip.aebc.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voip.blackberry.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voip.eutelia.it", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voiparound.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipblast.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipbuster.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipbusterpro.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipcheap.co.uk", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipcheap.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipfibre.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipgain.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipgate.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipinfocenter.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipplanet.nl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voippro.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipraider.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipstunt.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipwise.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voipzoom.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vopium.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voxgratia.org", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voxox.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voys.nl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.voztele.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.vyke.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.webcalldirect.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.whoi.edu", 3478));
        stunServers.Add(new Tuple<string, int>("stun.wifirst.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.wwdl.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun.xs4all.nl", 3478));
        stunServers.Add(new Tuple<string, int>("stun.xtratelecom.es", 3478));
        stunServers.Add(new Tuple<string, int>("stun.yesss.at", 3478));
        stunServers.Add(new Tuple<string, int>("stun.zadarma.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.zadv.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun.zoiper.com", 3478));
        stunServers.Add(new Tuple<string, int>("stun1.faktortel.com.au", 3478));
        stunServers.Add(new Tuple<string, int>("stun1.l.google.com", 19302));
        stunServers.Add(new Tuple<string, int>("stun1.voiceeclipse.net", 3478));
        stunServers.Add(new Tuple<string, int>("stun2.l.google.com", 19302));
        stunServers.Add(new Tuple<string, int>("stun3.l.google.com", 19302));
        stunServers.Add(new Tuple<string, int>("stun4.l.google.com", 19302));
        stunServers.Add(new Tuple<string, int>("stunserver.org", 3478));

        ConsoleEx.WriteLine("Contacting STUN servers to obtain your IP", ConsoleEx.Network());
        foreach (Tuple<string, int> server in stunServers)
        {
            string host = server.Item1;
            int port = server.Item2;
            //int port = 3478;

            StunResult externalEndPoint = StunClient.Query(host, port, socket);

            if (externalEndPoint.NetType == StunNetType.UdpBlocked)
            {
                continue;
            }

            ConsoleEx.WriteLine("Your firewall is " + externalEndPoint.NetType.ToString(), ConsoleEx.Network());

            return new P2pEndPoint()
            {
                External = externalEndPoint.PublicEndPoint,
                Internal = (socket.LocalEndPoint as IPEndPoint)
            };
        }

        ConsoleEx.WriteLine("Could not find a working STUN server", ConsoleEx.Error());
        return null;
    }
}
