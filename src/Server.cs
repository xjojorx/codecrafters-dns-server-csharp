using System.Buffers.Binary;
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
    var response = HandleMessage(receivedData, sourceEndPoint);

    // Send response
    udpClient.Send(response, response.Length, sourceEndPoint);
}

///////////////////////////////////////
/// logic
///////////////////////////////////////

static byte[] HandleMessage(byte[] data, IPEndPoint sourceEndPoint)
{
    string receivedString = Encoding.ASCII.GetString(data);

    Console.WriteLine($"Received {data.Length} bytes from {sourceEndPoint}: {receivedString}");

    var req = ParseRequest(data);

    var res = BuildResponse(req);

    var response = EncodeResponse(res);

    return response;
}


///////////////////////////////////////
/// functions
///////////////////////////////////////
static DNSRequest ParseRequest(ReadOnlySpan<byte> data) {

    var reqHeader = ParseHeader(data.Slice(0,12));

    int idx = 12;
    List<DNSQuestion> questions = [];
    for (int i = 0; i < reqHeader.QuestionCount; i++)
    {
        var (question, read) = ParseQuestion(data.Slice(idx), data);
        questions.Add(question);
        idx += read;
    }


    var req = new DNSRequest(reqHeader, questions);
    return req;
}

static DNSResponse BuildResponse(DNSRequest request)
{
    // Create an empty response
    var header = InitHeader(request);
    var question = InitQuestion(request);
    var answer = InitAnswer(request);

    return new DNSResponse(header, question, answer);
}

static DNSHeader InitHeader(DNSRequest request)
{
    ResponseCodes rc = request.Header.Opcode switch {
        0 => ResponseCodes.Ok,
        _ => ResponseCodes.NotImplemented
    };
    DNSHeader dNSHeader = new DNSHeader(request.Header.ID, QRId.Response, request.Header.Opcode, false, false, request.Header.RecursionDesired, false, Reserved.None, rc, request.Header.QuestionCount, request.Header.QuestionCount, 0, 0);

    return dNSHeader;
}

static List<DNSQuestion> InitQuestion(DNSRequest request)
{
    return request.Questions.Select(q => {
            // string name = "codecrafters.io";
            // var labels = name.Split('.').ToList();
            var labels = q.Labels;
            var type = RecordTypes.A;
            var qclass = RecordClasses.IN;
            return new DNSQuestion(labels, type, qclass);
    }).ToList();
}

static List<DNSAnswer> InitAnswer(DNSRequest request)
{
    return request.Questions.Select(q => {
            var labels = q.Labels;
            var type = RecordTypes.A;
            var qclass = RecordClasses.IN;
            byte[] dataBytes = [0x08, 0x08, 0x08, 0x08];
            short len = (short)dataBytes.Length;
            int ttl = 60;

            return new DNSAnswer(labels, type, qclass, ttl, len, dataBytes);
            }).ToList();
}

static byte[] EncodeResponse(DNSResponse response)
{
    var header = EncodeHeader(response.Header);
    var question = EncodeQuestions(response.Questions);
    var answer = EncodeAnswers(response.Answers);

    using var ms = new MemoryStream();
    ms.Write(header);
    ms.Write(question);
    ms.Write(answer);
    return ms.ToArray();
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

    b |= (byte)((((int)header.Opcode) & 15) << 3);
    b |= header.IsAuthoritative switch
    {
        true => 1 << 2,
        _ => 0
    };
    b |= header.Truncation switch
    {
        true => 1 << 1,
        _ => 0
    };
    b |= header.RecursionDesired switch
    {
        true => 1,
        _ => 0
    };
    buf[2] = b;

    //fourth byte
    b = 0;

    b |= header.RecursionAvailable switch
    {
        true => 1 << 7,
        _ => 0
    };

    var reservedVal = (int)header.Reserved;
    b |= (byte)(reservedVal << 4);
    b |= (byte)(((int)header.ResponseCode) & 0x0f);

    buf[3] = b;

    //last bytes

    (buf[4], buf[5]) = SplitShort(header.QuestionCount);
    (buf[6], buf[7]) = SplitShort(header.AnswerRecordCount);
    (buf[8], buf[9]) = SplitShort(header.AuthorityRecordCount);
    (buf[10], buf[11]) = SplitShort(header.AdditionalRecordCount);

    return buf;
}
static DNSHeader ParseHeader(ReadOnlySpan<byte> data)
{
    //first 2 bytes -> short id
    short id = JoinShort(data.Slice(0, 2));

    //byte 3
    bool qrSet = (data[2] & (1 << 7)) != 0;
    QRId qr = qrSet ? QRId.Query : QRId.Response;

    int opcodeval = (data[2] >> 3) & 0x0f;
    Opcodes opcode = (Opcodes)opcodeval;


    bool authoritative = (data[2] & (1 << 2)) != 0;
    bool truncation = (data[2] & (1 << 1)) != 0;
    bool recursionDesired = (data[2] & (1 << 0)) != 0;

    //byte 4
    bool recursionAvailable = (data[3] & (1 << 7)) != 0;
    int reservedVal = (data[3] >> 4) & (1 << 3);
    Reserved reserved = (Reserved)reservedVal;
    int responseCodeVal = data[3] & 0x0f;
    ResponseCodes responseCode = (ResponseCodes)responseCodeVal;

    short qdcount = JoinShort(data.Slice(4, 2));
    short ancount = JoinShort(data.Slice(6, 2));
    short nscount = JoinShort(data.Slice(8, 2));
    short arcount = JoinShort(data.Slice(10, 2));


    return new DNSHeader(id, qr, opcode, authoritative, truncation, recursionDesired, recursionAvailable, reserved, responseCode, qdcount, ancount, nscount, arcount);
}


static byte[] EncodeQuestions(IList<DNSQuestion> questions) => questions.SelectMany(EncodeQuestion).ToArray();
static byte[] EncodeQuestion(DNSQuestion question)
{
    var nameBytes = question.Labels
      .Select(EncodeLabel)
      .Cast<IEnumerable<byte>>()
      .Aggregate((acc, curr) => acc.Concat(curr))
      .ToArray();

    var typeBytes = new byte[2];
    (typeBytes[0], typeBytes[1]) = SplitShort((short)question.RecordType);
    var classBytes = new byte[2];
    (classBytes[0], classBytes[1]) = SplitShort((short)question.Class);

    byte[] res = [.. nameBytes, 0, .. typeBytes, .. classBytes];
    return res;
}
static byte[] EncodeLabel(string label)
{
    byte[] labelBytes = Encoding.ASCII.GetBytes(label);
    return [(byte)labelBytes.Length, .. labelBytes];
}

static (DNSQuestion q, int readBytes) ParseQuestion(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> fullBuffer) {
    //read labels (read until 0x00;

    List<String> labels = [];
    int idx = 0;
    bool labelsEnd = false;
    while(!labelsEnd) {
        var len = buffer[idx];
        switch (len)
        {
            case 0x00:
                idx++;
                labelsEnd = true;
                break;
            case var p when (p & 0xc0) != 0:
                int ptr = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(idx)) & 0x3FFF;
                // var ptr = BinaryPrimitives.ReadUInt16BigEndian(buffer) & 0x3FFF;
                idx += 2;
                var refLen = fullBuffer[ptr];
                var refLBytes = fullBuffer.Slice(ptr+1, refLen);
                var refLabel = Encoding.ASCII.GetString(refLBytes);
                labels.Add(refLabel);
                labelsEnd = true;
                break;
            default:
                idx++;
                var labelBytes = buffer.Slice(idx,len);
                var label = Encoding.ASCII.GetString(labelBytes);
                labels.Add(label);
                idx += len;
                break;
        }
    }

    var typeVal = BinaryPrimitives.ReadInt16BigEndian(buffer[idx..]);
    var type = (RecordTypes) typeVal;
    idx +=2;

    var classVal = BinaryPrimitives.ReadInt16BigEndian(buffer[idx..]);
    var rclass = (RecordClasses) classVal;
    idx +=2;

    return (new DNSQuestion(labels, type, rclass), idx);
}

static byte[] EncodeAnswers(IList<DNSAnswer> questions) => questions.SelectMany(EncodeAnswer).ToArray();
static byte[] EncodeAnswer(DNSAnswer answer)
{
    var nameBytes = answer.Labels
      .Select(EncodeLabel)
      .Cast<IEnumerable<byte>>()
      .Aggregate((acc, curr) => acc.Concat(curr))
      .ToArray();

    var typeBytes = new byte[2];
    (typeBytes[0], typeBytes[1]) = SplitShort((short)answer.RecordType);
    var classBytes = new byte[2];
    (classBytes[0], classBytes[1]) = SplitShort((short)answer.Class);

    var ttlBytes = SplitInt(answer.TTL);

    var lenBytes = new byte[2];
    (lenBytes[0], lenBytes[1]) = SplitShort((short)answer.Length);

    byte[] res = [.. nameBytes, 0, .. typeBytes, .. classBytes, .. ttlBytes, .. lenBytes, .. answer.Data];
    return res;
}





static (byte, byte) SplitShort(short input)
{

    byte b1, b2;

    b1 = (byte)(input >> 8);
    b2 = (byte)(input & 0xff);

    return (b1, b2);
}
static short JoinShort(ReadOnlySpan<byte> bytes)
{
    short n = (short)((bytes[0] << 8) | bytes[1]);
    return n;
}

static byte[] SplitInt(int input)
{
    byte b1, b2, b3, b4;

    b1 = (byte)(input >> 24);
    b2 = (byte)(input >> 16);
    b3 = (byte)(input >> 8);
    b4 = (byte)(input & 0xff);

    return [b1, b2, b3, b4];
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
    NotImplemented = 4,
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

enum RecordTypes : short
{
    A = 1,
    CNAME = 5,
}
enum RecordClasses : short
{
    IN = 1,
}
record DNSQuestion(List<string> Labels, RecordTypes RecordType, RecordClasses Class);

record DNSAnswer(List<string> Labels, RecordTypes RecordType, RecordClasses Class, int TTL, short Length, byte[] Data);

record DNSResponse(DNSHeader Header, List<DNSQuestion> Questions, List<DNSAnswer> Answers);
record DNSRequest(DNSHeader Header, List<DNSQuestion> Questions);
