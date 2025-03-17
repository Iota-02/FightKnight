using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerSpawner : NetworkBehaviour
{
    public GameObject playerPrefab;
    public Transform hostSpawnPoint;
    public Transform clientSpawnPoint;

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log("[NetworkManager] Client connected: " + clientId);
    }

    void OnClientDisconnected(ulong clientId)
    {
        Debug.Log("[NetworkManager] Client disconnected: " + clientId);
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log("[PlayerSpawner] OnNetworkSpawn called. IsServer: " + IsServer + " IsClient: " + IsClient);

        if (IsServer)
        {
            Debug.Log("[PlayerSpawner] Server spawning players.");
            SpawnPlayers();
        }
        else if (IsClient)
        {
            Debug.Log("[PlayerSpawner] Client requesting spawn.");
            StartCoroutine(RequestSpawn());
        }
    }

    IEnumerator RequestSpawn()
    {
        yield return new WaitForSeconds(0.2f);
        Debug.Log("[PlayerSpawner] [Client] Requesting spawn. My ClientId is: " + NetworkManager.Singleton.LocalClientId);
        RequestSpawnPlayerServerRpc();
    }

    private void SpawnPlayers()
    {
        Debug.Log("[PlayerSpawner] [Server] SpawnPlayers() called. Connected clients: " + NetworkManager.Singleton.ConnectedClientsList.Count);

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            Debug.Log("[PlayerSpawner] [Server] Spawning player for client ID: " + client.ClientId);
            Transform spawnPoint = client.ClientId == NetworkManager.ServerClientId ? hostSpawnPoint : clientSpawnPoint;
            Debug.Log("[PlayerSpawner] [Server] Using spawn point: " + spawnPoint.name);

            GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            Debug.Log("[PlayerSpawner] [Server] Player instantiated for client: " + client.ClientId);

            NetworkObject networkObject = player.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                networkObject.SpawnAsPlayerObject(client.ClientId, true);
                Debug.Log("[PlayerSpawner] [Server] Player spawned as player object for client ID: " + client.ClientId + " is owned by: " + networkObject.OwnerClientId);
            }
            else
            {
                Debug.LogError("[PlayerSpawner] [Server] Player prefab does not have a NetworkObject component.");
                Destroy(player);
            }
        }
    }

    [ServerRpc]
    private void RequestSpawnPlayerServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        Debug.Log("[PlayerSpawner] [Server] Spawn request received from client ID: " + senderClientId);

        if (NetworkManager.Singleton.ConnectedClients.ContainsKey(senderClientId))
        {
            Debug.Log("[PlayerSpawner] [Server] Client ID " + senderClientId + " found. Spawning players.");
            SpawnPlayers();
        }
        else
        {
            Debug.LogError("[PlayerSpawner] [Server] Client ID " + senderClientId + " not found.");
        }
    }
}