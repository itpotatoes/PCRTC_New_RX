using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using LZ4;
using Unity.WebRTC;
using UnityEngine;
using WebSocketSharp;
using Debug = UnityEngine.Debug;
 
public class UnityWebRTC : MonoBehaviour
{
    private RTCPeerConnection peerConnection;
    private RTCDataChannel dataChannel;
    private int expectedDataSize = 1474568;

    [SerializeField] private string signalingUrl = "ws://lyj.leafserver.kr:3000"; // 시그널링 서버 주소

    private Mesh mesh;
    private Vector3[] vertices;
    private Color32[] colors;
    private int[] indices;
    private int num = 92160;
    private WebSocket webSocket;

    
    void Start()
    {
        InitMesh();
        //WebRTC 초기화
        WebRTC.Initialize();
        // ICE 서버 구성
        RTCConfiguration config = new RTCConfiguration();
        config.iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun2.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun3.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun4.l.google.com:19302" } }
        };
        peerConnection = new RTCPeerConnection(ref config);
        RTCDataChannelInit dataconfig = new RTCDataChannelInit();
        dataChannel = peerConnection.CreateDataChannel("data",dataconfig);
        Debug.Log("Data channel created.");
        dataChannel.OnOpen += () =>
        {
            Debug.Log("Data channel opened (dataChannel)");
        };
        dataChannel.OnClose += () =>
        {
            Debug.Log("Data channel closed (dataChannel)");
        };
        dataChannel.OnMessage += (byte[] data) =>
        {
           ReceiveChunk(data);
            
        };
        
        peerConnection.OnDataChannel = channel =>
        {
            dataChannel = channel;
        };
        
        
        // Add ICE candidate event handler
        peerConnection.OnIceCandidate += OnIceCandidate;
        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log(state);
        };

        
        //시그널링 서버와 연결
        webSocket = new WebSocket(signalingUrl);
        webSocket.OnOpen += (sender, e) =>
        {
            Debug.Log("WebSocket connection opened.");
        };
        webSocket.OnMessage += OnWebSocketMessage;
        webSocket.OnError += (sender, e) => Debug.LogError("WebSocket error: " + e.Exception);
        webSocket.OnClose += (sender, e) => Debug.Log("WebSocket connection closed.");

        webSocket.ConnectAsync();

        

    }
    
    private void InitMesh()
    {
        // 가로 세로 취득
        int width = 320;
        int height = 288;
        num = width * height;
        mesh = new Mesh();
        
        //65535 점 이상을 표현하기 위해 설정
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        
        // 저장공간 확보
        vertices = new Vector3[num];
        colors = new Color32[num];
        indices = new int[num];
        
        //PointCloud 배열 번호를 기록

        for (int i = 0; i < num; i++)
        {
            indices[i] = i;
        }

        //mesh에 값 전달
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.SetIndices(indices,MeshTopology.Points,0);
        
        //메쉬를 MeshFilter에 적용
        gameObject.GetComponent<MeshFilter>().mesh = mesh;
    }
    
    
    private byte[] receivedData;
    private int receivedChunks;

    private byte[] receiveBuffer = null;
    private int receivedDataSize = -1;

    private List<byte> receivedData2 = new List<byte>();
 
    int exceptionCounter = 0;

private MemoryStream ms = new MemoryStream();

/*
        for (int i = 0; i < numVertices; i++)
        {
            int startIndex = i * (sizeof(float) * 3 + sizeof(byte) * 4);

            // 각 값의 바이트 배열 길이를 가져옴
            byte[] xBytes = new byte[sizeof(float)];
            byte[] yBytes = new byte[sizeof(float)];
            byte[] zBytes = new byte[sizeof(float)];
            byte[] colorBytes = new byte[sizeof(byte) * 4];

            Buffer.BlockCopy(receivedData, startIndex, xBytes, 0, sizeof(float));
            Buffer.BlockCopy(receivedData, startIndex + sizeof(float), yBytes, 0, sizeof(float));
            Buffer.BlockCopy(receivedData, startIndex + sizeof(float) * 2, zBytes, 0, sizeof(float));
            Buffer.BlockCopy(receivedData, startIndex + sizeof(float) * 3, colorBytes, 0, sizeof(byte) * 4);

            // 바이트 배열을 해당 형식으로 변환합니다.
            float x = BitConverter.ToSingle(xBytes, 0);
            float y = BitConverter.ToSingle(yBytes, 0);
            float z = BitConverter.ToSingle(zBytes, 0);
            byte r = colorBytes[0];`
            byte g = colorBytes[1];
            byte b = colorBytes[2];
            byte a = colorBytes[3];

            // 역직렬화된 값을 배열에 저장
            vertices[i] = new Vector3(x, y, z);
            colors[i] = new Color32(r, g, b, a);
            
        }
       long unixTimestamp = BitConverter.ToInt64(timestampBytes, 0);            
        */

    private int reCount = 0;
    private int Count = 0;
    
    
    public byte[] DecompressData2(byte[] compressedData)
    {
        try
        {
            using (MemoryStream input = new MemoryStream(compressedData))
            using (MemoryStream output = new MemoryStream())
            {
                using (LZ4Stream lz4Stream = new LZ4Stream(input, LZ4StreamMode.Decompress))
                {
                    lz4Stream.CopyTo(output);
                }
                return output.ToArray();
            }
        }
        catch (NullReferenceException nre)
        {
            Debug.LogError("Null reference encountered: " + nre.Message);
            Debug.LogError(nre.StackTrace); // Log stack trace
            return null; 
        }
        catch (ArgumentException ae)
        {
            Debug.LogError("Data might be corrupted: " + ae.Message);
            Debug.LogError(ae.StackTrace); // Log stack trace
            return null; 
        }
        catch (Exception e)
        {
            Debug.LogError("An error occurred during decompression: " + e.Message);
            Debug.LogError(e.StackTrace); // Log stack trace
            return null;
        }
    }

    
    private async void ReceiveChunk(byte[] chunkData)
    {
        // 특별한 종료 신호가 오면 데이터 처리를 시작
        if (chunkData.Length == 4 && BitConverter.ToInt32(chunkData, 0) == 0)
        {
            try
            {
                byte[] receivedData = receivedData2.ToArray();
                

                Vector3[] vertices = new Vector3[92160];
                Color32[] colors = new Color32[92160];

                for (int i = 0; i < 92160; i++)
                {
                    int offset = i * (sizeof(float) * 3 + sizeof(byte) * 4);
                    float x = BitConverter.ToSingle(receivedData, offset);
                    float y = BitConverter.ToSingle(receivedData, offset + sizeof(float));
                    float z = BitConverter.ToSingle(receivedData, offset + 2 * sizeof(float));
                
                    byte r = receivedData[offset + 3 * sizeof(float)];
                    byte g = receivedData[offset + 3 * sizeof(float) + 1];
                    byte b = receivedData[offset + 3 * sizeof(float) + 2];
                    byte a = receivedData[offset + 3 * sizeof(float) + 3];

                    vertices[i] = new Vector3(x, y, z);
                    colors[i] = new Color32(r, g, b, a);
                }

                mesh.vertices = vertices;
                mesh.colors32 = colors;
                mesh.RecalculateBounds();

                // 데이터 처리 완료 후 receivedData2 초기화
                receivedData2.Clear();
            }
            catch(Exception e)
            {
                Debug.LogError("An error occurred while processing the received data: " + e.ToString());
                receivedData2.Clear();  // 에러 발생 시 데이터 초기화
            }
        }
        else
        {
            // 특별한 종료 신호가 아니면 데이터를 계속 모음
            receivedData2.AddRange(chunkData);
        }
    }


private int frameCount = 0;
private Stopwatch fpsStopwatch = new Stopwatch();

    private IEnumerator CreateOfferCoroutine()
    {
        var op = peerConnection.CreateOffer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError("Failed to create SDP offer: " + op.Error);
            yield break;
        }

        var offer = op.Desc;
        peerConnection.SetLocalDescription(ref offer);
        var jsonOffer = JsonUtility.ToJson(offer);
        if (webSocket.ReadyState == WebSocketState.Open)
        {
            Debug.Log("Create Offer");
            webSocket.Send(jsonOffer);
        }
        else
        {
            Debug.LogError("WebSocket state: " + webSocket.ReadyState);
            Debug.LogError("WebSocket is not open. Cannot send offer.");
        }
    }
    
    
      private void OnWebSocketMessage(object sender, MessageEventArgs e)
    {
        string dataAsString = Encoding.UTF8.GetString(e.RawData);
        Debug.Log("WebRTC_RX : Received message from signaling server as string: " + dataAsString);
        // Deserialize the received message
        var message = JsonUtility.FromJson<SignalingMessage>(dataAsString);
        Debug.Log("수신받은 데이터"+"type: " + message.type + "candidate: " + message.candidate + "sdp: " + message.sdp);
    
        if (!string.IsNullOrEmpty(message.sdp))
        {
            // Convert message.type to RTCSdpType
            if (System.Enum.TryParse<RTCSdpType>(message.type, true, out var sdpType))
            {
                // The message is an SDP
                var sessionDesc = new RTCSessionDescription { type = sdpType, sdp = message.sdp };
                if (sdpType == RTCSdpType.Offer)
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        Debug.Log($"OnWebSocketMessage: {sdpType}");
                        StartCoroutine(SetRemoteDescriptionAndCreateAnswerCoroutine(sessionDesc));
                    });
                }
                else if (sdpType == RTCSdpType.Answer)
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        Debug.Log($"OnWebSocketMessage: {sdpType}");
                        StartCoroutine(SetRemoteDescriptionCoroutine(sessionDesc));
                    });
                }
            }
            else
            {
                Debug.LogError($"Failed to parse SDP type: {message.type}");
            }
        }
        else if (!string.IsNullOrEmpty(message.candidate))
        {
            RTCIceCandidateInit iceCandidateInit = new RTCIceCandidateInit
            {
                candidate = message.candidate,
                sdpMid = message.sdpMid,
                sdpMLineIndex = message.sdpMLineIndex.GetValueOrDefault()
            };
            RTCIceCandidate iceCandidate = new RTCIceCandidate(iceCandidateInit);
            peerConnection.AddIceCandidate(iceCandidate);
        }   
        else if (message.type == "start")
        {
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                // 로컬 피어가 Offer를 생성하도록 설정
                StartCoroutine(CreateOfferCoroutine());
            });
        }
    }
      private IEnumerator SetRemoteDescriptionCoroutine(RTCSessionDescription sessionDesc)
      {
          var opSetRemoteDesc = peerConnection.SetRemoteDescription(ref sessionDesc);
          yield return opSetRemoteDesc;

          if (opSetRemoteDesc.IsError)
          {
              Debug.LogError("Failed to set remote description: " + opSetRemoteDesc.Error.message);

              yield break;
          }
      }

    private IEnumerator CreateAnswerCoroutine()
    {
        var opCreateAnswer = peerConnection.CreateAnswer();
        yield return opCreateAnswer;

        if (opCreateAnswer.IsError)
        {
            Debug.LogError("Failed to create an answer: " + opCreateAnswer.Error.message);
            yield break;
        }

        var localAnswer = opCreateAnswer.Desc;
        var opSetLocalDesc = peerConnection.SetLocalDescription(ref localAnswer);
        yield return opSetLocalDesc;

        if (opSetLocalDesc.IsError)
        {
            Debug.LogError("Failed to set local description: " + opSetLocalDesc.Error.message);
            yield break;
        }
        SignalingMessage message = new SignalingMessage
        {
            type = localAnswer.type.ToString().ToLower(),
            sdp = localAnswer.sdp
        };
        // Send the answer via the signaling server.
        Debug.Log("Send Local description: " + message.type + message.sdp);
        SendSignalingMessage(message);
        
    }
    
    private void OnIceCandidate(RTCIceCandidate iceCandidate)
     {
       Debug.Log("Local ICE candidate: " + iceCandidate.Candidate);
       SignalingMessage message = new SignalingMessage
       {
           candidate = iceCandidate.Candidate,
           sdpMid = iceCandidate.SdpMid,
           sdpMLineIndex = iceCandidate.SdpMLineIndex
       };
       SendSignalingMessage(message);
      }
    

    [System.Serializable]
    private class SignalingMessage
    {
        public string type;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int? sdpMLineIndex;
    }

    private void SendSignalingMessage(object message)
    {
        if (webSocket != null && webSocket.IsAlive)
        {
            string jsonMessage = JsonUtility.ToJson(message);
            webSocket.Send(jsonMessage);
        }

    }
    private void OnDestroy()
    {
        // Close the data channel
        if (dataChannel != null)
        {
            dataChannel.Close();
            dataChannel.Dispose();
        }

        // Close the peer connection
        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
        }

        // Close the WebSocket connection
        if (webSocket != null && webSocket.IsAlive)
        {
            webSocket.Close();
            webSocket = null;
        }

        // Clean up WebRTC resources
        WebRTC.Dispose();
    }
    
    private IEnumerator SetRemoteDescriptionAndCreateAnswerCoroutine(RTCSessionDescription sessionDesc)
    {
        var opSetRemoteDesc = peerConnection.SetRemoteDescription(ref sessionDesc);
        yield return opSetRemoteDesc;

        if (opSetRemoteDesc.IsError)
        {
            Debug.LogError("Failed to set remote description: " + opSetRemoteDesc.Error.message);
            yield break;
        }

        if (sessionDesc.type == RTCSdpType.Offer)
        {
            StartCoroutine(CreateAnswerCoroutine());
        }
    }

}