using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using TMPro;

public class UserNametag : MonoBehaviour {

    public TMP_Text usernameText, starText;
    private PlayerController parent;

    void Start() {
        parent = GetComponentInParent<PlayerController>();
        gameObject.SetActive(!parent.photonView.IsMine);
    }
    
    void Update() {
        transform.localPosition = new Vector2(0, parent.hitboxes[0].size.y);

        usernameText.text = (parent.photonView.Owner.IsMasterClient ? "<sprite=5>" : "") + parent.photonView.Owner.NickName;
        // this will have to be updated if another character is added
        starText.text = (parent.lives > 0 ? (Utils.GetCharacterIndex(parent.photonView.Owner) == 0 ? "<sprite=3>" : "<sprite=4>") + parent.lives : "");
        starText.text += "<sprite=0>" + parent.stars;
    }
}
