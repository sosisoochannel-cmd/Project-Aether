using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>Lightweight presentation motion for the generated hero: breathing, run lean and landing squash.</summary>
    public sealed class PlayerHeroVisual : MonoBehaviour
    {
        private Transform _body, _head, _core, _eyeL, _eyeR;
        private Vector3[] _base;
        private float _landPulse;

        public void Configure(Transform body, Transform head, Transform core, Transform eyeL, Transform eyeR)
        {
            _body=body; _head=head; _core=core; _eyeL=eyeL; _eyeR=eyeR;
            _base=new[]{body.localPosition,head.localPosition,core.localPosition,eyeL.localPosition,eyeR.localPosition};
        }

        public void PulseLand() => _landPulse=1f;

        private void Update()
        {
            if (_base == null) return;
            var motor=GetComponent<PlayerMotor>();
            float speed=motor != null ? Mathf.Abs(motor.VelocityX) : 0f;
            bool grounded=motor != null && motor.IsGrounded;
            float bob=grounded ? Mathf.Sin(Time.time*(speed>0.2f?13f:4f))*(speed>0.2f?0.018f:0.008f) : 0f;
            float lean=grounded ? Mathf.Clamp(motor.VelocityX*0.018f,-0.08f,0.08f) : 0f;
            _landPulse=Mathf.MoveTowards(_landPulse,0f,Time.deltaTime*5.5f);
            float squash=1f-Mathf.Sin(_landPulse*Mathf.PI)*0.055f;

            Apply(_body,_base[0],bob,lean,squash);
            Apply(_head,_base[1],bob*1.15f,lean*0.55f,1f+(1f-squash)*0.2f);
            Apply(_core,_base[2],bob*0.8f,lean*0.7f,1f);
            Apply(_eyeL,_base[3],bob*1.15f,lean*0.55f,1f);
            Apply(_eyeR,_base[4],bob*1.15f,lean*0.55f,1f);
        }

        private static void Apply(Transform t,Vector3 origin,float y,float zLean,float squash)
        {
            if(t==null)return;
            t.localPosition=origin+new Vector3(0f,y,0f);
            t.localRotation=Quaternion.Euler(0f,0f,-zLean*45f);
            t.localScale=new Vector3(t.localScale.x*squash,t.localScale.y*(2f-squash),t.localScale.z);
        }
    }
}
