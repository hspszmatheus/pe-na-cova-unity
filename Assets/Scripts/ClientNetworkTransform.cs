using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // Isso inverte a regra do Netcode, permitindo que o Cliente (IsOwner) controle sua própria posição
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}