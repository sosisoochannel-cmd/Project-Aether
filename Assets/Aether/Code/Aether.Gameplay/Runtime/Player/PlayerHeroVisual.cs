using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>Lightweight presentation motion for the generated hero: breathing, run lean and landing squash.</summary>
    public sealed class PlayerHeroVisual : MonoBehaviour
    {
        private Transform _body, _head, _core, _eyeL, _eyeR;
        private Vector3[] _base;
        private Vector3[] _scale;
        private float _landPulse;
        private PlayerMotor _motor;

        public void Configure(Transform body, Transform head, Transform core, Transform eyeL, Transform eyeR)
        {
            _body=body; _head=head; _core=core; _eyeL=eyeL; _eyeR=eyeR;
            _motor = GetComponent<PlayerMotor>();
            _base=new[]{body.localPosition,head.localPosition,core.localPosition,eyeL.localPosition,eyeR.localPosition};
            _scale=new[]{body.localScale,head.localScale,core.localScale,eyeL.localScale,eyeR.localScale};
        }

        public void PulseLand() => _landPulse=1f;

        private void Update()
        {
            if (_base == null) return;
            float speed=_motor != null ? Mathf.Abs(_motor.VelocityX) : 0f;
            bool grounded=_motor != null && _motor.IsGrounded;
            float bob=grounded ? Mathf.Sin(Time.time*(speed>0.2f?13f:4f))*(speed>0.2f?0.018f:0.008f) : 0f;
            float lean=grounded ? Mathf.Clamp(_motor.VelocityX*0.018f,-0.08f,0.08f) : 0f;
            _landPulse=Mathf.MoveTowards(_landPulse,0f,Time.deltaTime*5.5f);
            float squash=1f-Mathf.Sin(_landPulse*Mathf.PI)*0.055f;

            Apply(_body,_base[0],_scale[0],bob,lean,squash);
            Apply(_head,_base[1],_scale[1],bob*1.15f,lean*0.55f,1f+(1f-squash)*0.2f);
            Apply(_core,_base[2],_scale[2],bob*0.8f,lean*0.7f,1f);
            Apply(_eyeL,_base[3],_scale[3],bob*1.15f,lean*0.55f,1f);
            Apply(_eyeR,_base[4],_scale[4],bob*1.15f,lean*0.55f,1f);
        }

        private static void Apply(Transform t,Vector3 origin,Vector3 scale,float y,float zLean,float squash)
        {
            if(t==null)return;
            t.localPosition=origin+new Vector3(0f,y,0f);
            t.localRotation=Quaternion.Euler(0f,0f,-zLean*45f);
            t.localScale=new Vector3(scale.x*squash,scale.y*(2f-squash),scale.z);
        }
    }
}
