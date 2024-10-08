using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Resolve UDP address
IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
int port = 2053;
IPEndPoint udpEndPoint = new IPEndPoint(ipAddress, port);

// Create UDP socket
UdpClient udpClient = new UdpClient(udpEndPoint);

while (true)
{
  // Receive data
  IPEndPoint sourceEndPoint = new IPEndPoint(IPAddress.Any, 0);
  byte[] receivedData = udpClient.Receive(ref sourceEndPoint);
  string receivedString = Encoding.ASCII.GetString(receivedData);

  Console.WriteLine($"Received {receivedData.Length} bytes from {sourceEndPoint}: {receivedString}");

  // Create an empty response
  // byte[] response = Encoding.ASCII.GetBytes("");
  var header = InitHeader();
  byte[] response = EncodeHeader(header);
  // Console.WriteLine(header);
  // Console.WriteLine(Convert.ToHexString(response));

  // Send response
  udpClient.Send(response, response.Length, sourceEndPoint);
}


///////////////////////////////////////
/// functions
///////////////////////////////////////

static DNSHeader InitHeader()
{
  var id = GeneratePacketId();
  DNSHeader dNSHeader = new DNSHeader(id, QRId.Response, 0, false, false, false, false, Reserved.None, ResponseCodes.Ok, 0, 0, 0, 0);

  return dNSHeader;
}

static short GeneratePacketId()
{
  // int rnd = Random.Shared.Next();
  // return (short)rnd;
  return 1234;
}

static byte[] EncodeHeader(DNSHeader header)
{
  var buf = new byte[12];
  //2 bytes of id
  (buf[0], buf[1]) = SplitShort(header.ID);

  //third bytr
  byte b = 0;
  b |= header.QR switch
  {
    QRId.Query => 0,
    QRId.Response => 1 << 7,
    _ => throw new Exception("unexpected qrid")
  };

  b |= (byte)((((int)header.Opcode) & 15)<<3);
  b |= header.IsAuthoritative switch {
    true => 1 << 2,
    _ => 0
  };
  b |= header.Truncation switch {
    true => 1 << 1,
    _ => 0
  };
  b |= header.RecursionDesired switch {
    true => 1 ,
    _ => 0
  };
  buf[2] = b;

  //fourth byte
  b = 0;

  b |= header.RecursionAvailable switch {
    true => 1 <<7,
    _ => 0
  };

  var reservedVal = (int)header.Reserved;
  b |= (byte)(reservedVal << 4);
  b |= (byte)( ((int)header.ResponseCode) & 0x0f);
  
  buf[3] = b;

  //last bytes

  (buf[4], buf[5]) = SplitShort(header.QuestionCount);
  (buf[6], buf[7]) = SplitShort(header.AnswerRecordCount);
  (buf[8], buf[9]) = SplitShort(header.AuthorityRecordCount);
  (buf[10], buf[11]) = SplitShort(header.AdditionalRecordCount);

  return buf;
}
static (byte, byte) SplitShort(short input) {

  byte b1,b2;

  b1 = (byte)(input >> 8);
  b2 = (byte)(input & 0xff);

  return (b1, b2);
}


///////////////////////////////////////
/// types
///////////////////////////////////////
enum QRId
{
  Query = 0,
  Response = 1,
}

enum Opcodes : int
{
  None = 0,
}

enum Reserved
{
  None = 0
}

enum ResponseCodes
{
  Ok = 0,
}

record DNSHeader(
    short ID,
    QRId QR,
    Opcodes Opcode,
    bool IsAuthoritative,
    bool Truncation,
    bool RecursionDesired,
    bool RecursionAvailable,
    Reserved Reserved,
    ResponseCodes ResponseCode,
    short QuestionCount,
    short AnswerRecordCount,
    short AuthorityRecordCount,
    short AdditionalRecordCount
    );
